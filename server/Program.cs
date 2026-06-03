using System.Security.Claims;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using System.IO.Compression;
using LShopOzonWebReact.Api.Billing;
using LShopOzonWebReact.Api.Data;
using LShopOzonWebReact.Api.Hubs;
using LShopOzonWebReact.Api.Models;
using LShopOzonWebReact.Api.Ozon;
using LShopOzonWebReact.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.Configure<OzonOptions>(builder.Configuration.GetSection("Ozon"));
builder.Services.Configure<YooKassaOptions>(builder.Configuration.GetSection("YooKassa"));
builder.Services.AddHttpClient<OzonApiClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OzonOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});
builder.Services.AddHttpClient("YooKassa", client =>
{
    client.BaseAddress = new Uri("https://api.yookassa.ru");
});
builder.Services.AddScoped<JwtTokenService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var key = builder.Configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrWhiteSpace(accessToken) && path.StartsWithSegments("/hubs/live"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "data-protection-keys")));
builder.Services.AddSignalR();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactDev", policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var hasStaticClient = !string.IsNullOrWhiteSpace(app.Environment.WebRootPath)
    && Directory.Exists(app.Environment.WebRootPath);

app.UseForwardedHeaders();
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Https:UseRedirection"))
{
    app.UseHttpsRedirection();
}
if (hasStaticClient)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.UseCors("ReactDev");
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated != true
        || SubscriptionAccess.IsExemptPath(context.Request.Path))
    {
        await next();
        return;
    }

    using var scope = context.RequestServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var companyIdClaim = context.User.FindFirstValue("company_id");
    if (!Guid.TryParse(companyIdClaim, out var companyId))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(item => item.Id == companyId);
    if (company is null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    if (!SubscriptionAccess.IsActive(company))
    {
        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Демо-доступ закончился. Требуется подписка через ЮKassa.",
            company.SubscriptionStatus,
            company.TrialEndsAt,
            company.SubscriptionPaidUntil
        });
        return;
    }

    await next();
});

app.MapHub<AppHub>("/hubs/live").RequireAuthorization();

app.MapPost("/api/auth/register", async (RegisterCompanyRequest request, AppDbContext db, JwtTokenService tokenService) =>
{
    if (string.IsNullOrWhiteSpace(request.CompanyName)
        || string.IsNullOrWhiteSpace(request.UserName)
        || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest("Название компании, логин и пароль обязательны.");
    }

    var companyLoginName = CompanyAccess.NormalizeLoginName(request.CompanyName);
    if (await db.Companies.AnyAsync(company => company.LoginName == companyLoginName))
    {
        return Results.Conflict("Компания с таким названием уже зарегистрирована.");
    }

    var trialEndsAt = DateTimeOffset.UtcNow.AddDays(3);
    var company = new Company
    {
        Name = request.CompanyName.Trim(),
        LoginName = companyLoginName,
        SubscriptionStatus = CompanySubscriptionStatuses.Trial,
        TrialEndsAt = trialEndsAt
    };

    var admin = new AppUser
    {
        Company = company,
        UserName = request.UserName.Trim(),
        DisplayName = request.DisplayName.Trim(),
        PasswordHash = PasswordHasher.Hash(request.Password),
        Role = UserRoles.Admin
    };

    db.Companies.Add(company);
    db.Users.Add(admin);
    await db.SaveChangesAsync();

    return Results.Created("/api/auth/register", new AuthResponse(
        tokenService.CreateToken(admin),
        UserResponses.Current(admin)));
});

app.MapPost("/api/setup/admin", async (RegisterCompanyRequest request, AppDbContext db, JwtTokenService tokenService) =>
{
    if (await db.Users.AnyAsync())
    {
        return Results.Conflict("Первый админ уже создан.");
    }

    var companyLoginName = CompanyAccess.NormalizeLoginName(request.CompanyName);
    var trialEndsAt = DateTimeOffset.UtcNow.AddDays(3);
    var company = new Company
    {
        Name = request.CompanyName.Trim(),
        LoginName = companyLoginName,
        SubscriptionStatus = CompanySubscriptionStatuses.Trial,
        TrialEndsAt = trialEndsAt
    };

    var admin = new AppUser
    {
        Company = company,
        UserName = request.UserName.Trim(),
        DisplayName = request.DisplayName.Trim(),
        PasswordHash = PasswordHasher.Hash(request.Password),
        Role = UserRoles.Admin
    };

    db.Companies.Add(company);
    db.Users.Add(admin);
    await db.SaveChangesAsync();

    return Results.Created("/api/admin/users", new AuthResponse(
        tokenService.CreateToken(admin),
        UserResponses.Current(admin)));
});

var products = new[]
{
    new Product(1, "Ozon карточка товара", "Готова к публикации", 1290),
    new Product(2, "Складской остаток", "12 единиц в наличии", 3490),
    new Product(3, "Заказ клиента", "Ожидает обработки", 780)
};

app.MapGet("/api/avatars/{fileName}", (string fileName, IWebHostEnvironment environment) =>
{
    if (fileName != Path.GetFileName(fileName))
    {
        return Results.BadRequest();
    }

    var avatarPath = Path.Combine(AppPaths.GetAvatarDirectory(environment), fileName);
    if (!System.IO.File.Exists(avatarPath))
    {
        return Results.NotFound();
    }

    var extension = Path.GetExtension(fileName).ToLowerInvariant();
    var contentType = extension switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg"
    };

    return Results.File(avatarPath, contentType);
});

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    AppDbContext db,
    JwtTokenService tokenService) =>
{
    var companyLoginName = CompanyAccess.NormalizeLoginName(request.CompanyName);
    var user = await db.Users
        .Include(item => item.Company)
        .SingleOrDefaultAsync(item => item.UserName == request.UserName
            && item.Company.LoginName == companyLoginName);

    if (user is null || !user.IsActive || !PasswordHasher.Verify(request.Password, user.PasswordHash))
    {
        return Results.Unauthorized();
    }

    if (user.Role != UserRoles.Admin && string.IsNullOrWhiteSpace(user.AllowedFeatures))
    {
        user.AllowedFeatures = FeatureAccess.NormalizeForRole(user.Role, FeatureAccess.UserDefaults);
    }

    user.LastSeenAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new AuthResponse(
        tokenService.CreateToken(user),
        UserResponses.Current(user)));
});

app.MapPost("/api/auth/heartbeat", async (AppDbContext db, ClaimsPrincipal principal) =>
{
    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(currentUserId, out var userId))
    {
        return Results.Unauthorized();
    }

    var user = await db.Users.Include(item => item.Company).FirstOrDefaultAsync(item => item.Id == userId);
    if (user is null || !user.IsActive)
    {
        return Results.Unauthorized();
    }

    user.LastSeenAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/auth/logout", async (AppDbContext db, ClaimsPrincipal principal) =>
{
    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(currentUserId, out var userId))
    {
        var user = await db.Users.FindAsync(userId);
        if (user is not null)
        {
            user.LastSeenAt = null;
            await db.SaveChangesAsync();
        }
    }

    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/auth/me", async (AppDbContext db, ClaimsPrincipal principal) =>
{
    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(currentUserId, out var userId))
    {
        return Results.Unauthorized();
    }

    var user = await db.Users
        .AsNoTracking()
        .Include(item => item.Company)
        .FirstOrDefaultAsync(item => item.Id == userId && item.IsActive);
    return user is null ? Results.Unauthorized() : Results.Ok(UserResponses.Current(user));
}).RequireAuthorization();

app.MapPut("/api/profile", async (
    UpdateProfileRequest request,
    AppDbContext db,
    ClaimsPrincipal principal) =>
{
    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(currentUserId, out var userId))
    {
        return Results.Unauthorized();
    }

    var user = await db.Users.FindAsync(userId);
    if (user is null || !user.IsActive)
    {
        return Results.Unauthorized();
    }

    user.DisplayName = request.DisplayName.Trim();
    await db.SaveChangesAsync();

    return Results.Ok(UserResponses.Current(user));
}).RequireAuthorization();

app.MapPost("/api/profile/avatar", async (
    HttpRequest request,
    IWebHostEnvironment environment,
    AppDbContext db,
    ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(currentUserId, out var userId))
    {
        return Results.Unauthorized();
    }

    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Ожидается файл изображения.");
    }

    var user = await db.Users.Include(item => item.Company).FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
    if (user is null || !user.IsActive)
    {
        return Results.Unauthorized();
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("avatar");
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("Выберите фотографию.");
    }

    if (file.Length > 3 * 1024 * 1024)
    {
        return Results.BadRequest("Фотография должна быть меньше 3 МБ.");
    }

    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    if (!allowedExtensions.Contains(extension))
    {
        return Results.BadRequest("Поддерживаются jpg, png, webp и gif.");
    }

    var avatarDirectory = AppPaths.GetAvatarDirectory(environment);
    Directory.CreateDirectory(avatarDirectory);
    if (!string.IsNullOrWhiteSpace(user.AvatarFileName))
    {
        var oldPath = Path.Combine(avatarDirectory, user.AvatarFileName);
        if (System.IO.File.Exists(oldPath))
        {
            System.IO.File.Delete(oldPath);
        }
    }

    var fileName = $"{user.Id:N}{extension}";
    var fullPath = Path.Combine(avatarDirectory, fileName);
    await using (var stream = System.IO.File.Create(fullPath))
    {
        await file.CopyToAsync(stream, cancellationToken);
    }

    user.AvatarFileName = fileName;
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(UserResponses.Current(user));
}).DisableAntiforgery().RequireAuthorization();

app.MapGet("/api/admin/users", async (AppDbContext db, ClaimsPrincipal principal) =>
{
    var onlineAfter = DateTimeOffset.UtcNow.AddMinutes(-2);
    var companyId = CompanyAccess.GetCompanyId(principal);
    return await db.Users
        .Where(user => user.CompanyId == companyId)
        .OrderBy(user => user.UserName)
        .Select(user => new UserListItem(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Position,
            user.Role,
            UserResponses.AvatarUrl(user),
            UserResponses.Features(user),
            user.IsActive,
            user.CreatedAt,
            user.LastSeenAt,
            user.LastSeenAt >= onlineAfter))
        .ToListAsync();
})
    .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapPost("/api/admin/users", async (CreateUserRequest request, AppDbContext db, ClaimsPrincipal principal) =>
{
    if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest("Логин и пароль обязательны.");
    }

    var companyId = CompanyAccess.GetCompanyId(principal);
    var exists = await db.Users.AnyAsync(user => user.CompanyId == companyId && user.UserName == request.UserName);
    if (exists)
    {
        return Results.Conflict("Пользователь с таким логином уже есть.");
    }

    var role = request.Role == UserRoles.Admin ? UserRoles.Admin : UserRoles.User;
    var user = new AppUser
    {
        CompanyId = companyId,
        UserName = request.UserName.Trim(),
        DisplayName = request.DisplayName.Trim(),
        Position = request.Position.Trim(),
        AllowedFeatures = FeatureAccess.NormalizeForRole(role, request.AllowedFeatures),
        PasswordHash = PasswordHasher.Hash(request.Password),
        Role = role
    };

    db.Users.Add(user);
    AuditLogWriter.Add(db, principal, "Создание пользователя", "User", user.Id.ToString(), $"{user.UserName} ({user.Role})");
    await db.SaveChangesAsync();

    return Results.Created($"/api/admin/users/{user.Id}", new UserListItem(
        user.Id,
        user.UserName,
        user.DisplayName,
        user.Position,
        user.Role,
        UserResponses.AvatarUrl(user),
        UserResponses.Features(user),
        user.IsActive,
        user.CreatedAt,
        user.LastSeenAt,
        false));
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapPut("/api/admin/users/{id:guid}/settings", async (
    Guid id,
    UpdateUserSettingsRequest request,
    AppDbContext db,
    ClaimsPrincipal principal) =>
{
    var companyId = CompanyAccess.GetCompanyId(principal);
    var user = await db.Users.FirstOrDefaultAsync(item => item.Id == id && item.CompanyId == companyId);
    if (user is null)
    {
        return Results.NotFound();
    }

    var role = request.Role == UserRoles.Admin ? UserRoles.Admin : UserRoles.User;
    user.DisplayName = request.DisplayName.Trim();
    user.Position = request.Position.Trim();
    user.Role = role;
    user.AllowedFeatures = FeatureAccess.NormalizeForRole(role, request.AllowedFeatures);

    AuditLogWriter.Add(db, principal, "Настройки пользователя", "User", user.Id.ToString(), $"{user.UserName} ({user.Role})");
    await db.SaveChangesAsync();

    return Results.Ok(new UserListItem(
        user.Id,
        user.UserName,
        user.DisplayName,
        user.Position,
        user.Role,
        UserResponses.AvatarUrl(user),
        UserResponses.Features(user),
        user.IsActive,
        user.CreatedAt,
        user.LastSeenAt,
        false));
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapPut("/api/admin/users/{id:guid}/password", async (
    Guid id,
    ChangeUserPasswordRequest request,
    AppDbContext db,
    ClaimsPrincipal principal) =>
{
    if (string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest("Пароль обязателен.");
    }

    var companyId = CompanyAccess.GetCompanyId(principal);
    var user = await db.Users.FirstOrDefaultAsync(item => item.Id == id && item.CompanyId == companyId);
    if (user is null)
    {
        return Results.NotFound();
    }

    user.PasswordHash = PasswordHasher.Hash(request.Password);
    AuditLogWriter.Add(db, principal, "Смена пароля", "User", user.Id.ToString(), user.UserName);
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapDelete("/api/admin/users/{id:guid}", async (Guid id, AppDbContext db, ClaimsPrincipal principal) =>
{
    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (currentUserId == id.ToString())
    {
        return Results.BadRequest("Нельзя удалить самого себя.");
    }

    var companyId = CompanyAccess.GetCompanyId(principal);
    var user = await db.Users.FirstOrDefaultAsync(item => item.Id == id && item.CompanyId == companyId);
    if (user is null)
    {
        return Results.NotFound();
    }

    db.Users.Remove(user);
    AuditLogWriter.Add(db, principal, "Удаление пользователя", "User", user.Id.ToString(), user.UserName);
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapGet("/api/admin/audit-logs", async (
    string? search,
    string? action,
    string? entityType,
    AppDbContext db) =>
{
    var query = db.AuditLogs.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var value = search.Trim().ToLower();
        query = query.Where(log =>
            log.UserName.ToLower().Contains(value)
            || log.DisplayName.ToLower().Contains(value)
            || log.Action.ToLower().Contains(value)
            || log.EntityType.ToLower().Contains(value)
            || log.EntityId.ToLower().Contains(value)
            || log.Details.ToLower().Contains(value));
    }

    if (!string.IsNullOrWhiteSpace(action))
    {
        query = query.Where(log => log.Action == action);
    }

    if (!string.IsNullOrWhiteSpace(entityType))
    {
        query = query.Where(log => log.EntityType == entityType);
    }

    return await query
        .OrderByDescending(log => log.CreatedAt)
        .Take(300)
        .Select(log => new AuditLogListItem(
            log.Id,
            log.UserName,
            log.DisplayName,
            log.Action,
            log.EntityType,
            log.EntityId,
            log.Details,
            log.CreatedAt))
        .ToListAsync();
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapGet("/api/admin/audit-logs/export", async (AppDbContext db) =>
{
    var logs = await db.AuditLogs
        .AsNoTracking()
        .OrderByDescending(log => log.CreatedAt)
        .Take(5000)
        .ToListAsync();

    var builder = new StringBuilder();
    builder.AppendLine("Дата;Пользователь;Имя;Действие;Объект;ID;Детали");
    foreach (var log in logs)
    {
        builder.AppendLine(string.Join(';', [
            CsvExport.Cell(log.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
            CsvExport.Cell(log.UserName),
            CsvExport.Cell(log.DisplayName),
            CsvExport.Cell(log.Action),
            CsvExport.Cell(log.EntityType),
            CsvExport.Cell(log.EntityId),
            CsvExport.Cell(log.Details)
        ]));
    }

    return Results.File(
        Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray(),
        "text/csv; charset=utf-8",
        $"audit-log-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapGet("/api/admin/system-health", async (AppDbContext db) =>
{
    var process = Process.GetCurrentProcess();
    var dbOk = await db.Database.CanConnectAsync();

    return Results.Ok(new SystemHealthResponse(
        dbOk,
        DateTimeOffset.UtcNow,
        (DateTimeOffset.UtcNow - process.StartTime.ToUniversalTime()).ToString(),
        Environment.MachineName,
        Environment.Version.ToString()));
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapGet("/api/admin/backups", (IWebHostEnvironment environment) =>
{
    var backupDirectory = AppPaths.GetBackupDirectory(environment);
    if (!Directory.Exists(backupDirectory))
    {
        return Results.Ok(Array.Empty<BackupFileResponse>());
    }

    var files = Directory
        .EnumerateFiles(backupDirectory, "*.sql.gz", SearchOption.TopDirectoryOnly)
        .Select(path =>
        {
            var info = new FileInfo(path);
            return new BackupFileResponse(
                info.Name,
                info.Length,
                info.LastWriteTimeUtc);
        })
        .OrderByDescending(file => file.CreatedAt)
        .Take(30)
        .ToList();

    return Results.Ok(files);
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapGet("/api/admin/backups/{fileName}", (string fileName, IWebHostEnvironment environment) =>
{
    if (fileName != Path.GetFileName(fileName)
        || !fileName.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest("Некорректное имя файла.");
    }

    var backupDirectory = AppPaths.GetBackupDirectory(environment);
    var fullPath = Path.GetFullPath(Path.Combine(backupDirectory, fileName));
    if (!fullPath.StartsWith(Path.GetFullPath(backupDirectory), StringComparison.OrdinalIgnoreCase)
        || !System.IO.File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    return Results.File(fullPath, "application/gzip", fileName);
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapGet("/api/integrations/ozon", async (
    OzonApiClient ozonApi,
    Microsoft.Extensions.Options.IOptions<OzonOptions> options,
    AppDbContext db,
    ClaimsPrincipal principal,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    var protector = dataProtectionProvider.CreateProtector("Fulvero.OzonCredentials.v1");
    var value = options.Value;
    var credentials = await CompanyAccess.GetOzonCredentialsAsync(db, principal, protector, value);
    var configured = !string.IsNullOrWhiteSpace(credentials.ClientId)
        && !string.IsNullOrWhiteSpace(credentials.ApiKey);

    if (!configured)
    {
        return Results.Ok(new OzonIntegrationStatusResponse(
            false,
            false,
            "Ozon ID или API-ключ не заданы для компании.",
            value.BaseUrl,
            AppPublicText.MaskSecret(credentials.ClientId),
            AppPublicText.MaskSecret(credentials.ApiKey),
            DateTimeOffset.UtcNow));
    }

    try
    {
        ozonApi.UseCredentials(credentials.ClientId, credentials.ApiKey);
        var result = await ozonApi.GetProductListAsync(1, cancellationToken);
        return Results.Ok(new OzonIntegrationStatusResponse(
            true,
            true,
            $"Ozon API отвечает. Найдено товаров: {result.Total}",
            value.BaseUrl,
            AppPublicText.MaskSecret(credentials.ClientId),
            AppPublicText.MaskSecret(credentials.ApiKey),
            DateTimeOffset.UtcNow));
    }
    catch (Exception exception)
    {
        return Results.Ok(new OzonIntegrationStatusResponse(
            true,
            false,
            AppPublicText.GetPublicOzonError(exception),
            value.BaseUrl,
            AppPublicText.MaskSecret(credentials.ClientId),
            AppPublicText.MaskSecret(credentials.ApiKey),
            DateTimeOffset.UtcNow));
    }
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapPut("/api/integrations/ozon", async (
    UpdateOzonIntegrationRequest request,
    AppDbContext db,
    ClaimsPrincipal principal,
    IDataProtectionProvider dataProtectionProvider) =>
{
    if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ApiKey))
    {
        return Results.BadRequest("Ozon ID и API-ключ обязательны.");
    }

    var companyId = CompanyAccess.GetCompanyId(principal);
    var company = await db.Companies.FindAsync(companyId);
    if (company is null)
    {
        return Results.Unauthorized();
    }

    var protector = dataProtectionProvider.CreateProtector("Fulvero.OzonCredentials.v1");
    company.OzonClientIdProtected = protector.Protect(request.ClientId.Trim());
    company.OzonApiKeyProtected = protector.Protect(request.ApiKey.Trim());

    AuditLogWriter.Add(db, principal, "Настройка Ozon API", "Company", company.Id.ToString(), company.Name);
    await db.SaveChangesAsync();

    return Results.Ok(new OzonIntegrationStatusResponse(
        true,
        false,
        "Ozon ID и API-ключ сохранены. Нажмите проверку, чтобы убедиться, что доступ работает.",
        string.Empty,
        AppPublicText.MaskSecret(request.ClientId),
        AppPublicText.MaskSecret(request.ApiKey),
        DateTimeOffset.UtcNow));
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapGet("/api/chat/users", async (AppDbContext db, ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Chats))
    {
        return Results.Forbid();
    }

    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(currentUserId, out var userId))
    {
        return Results.Unauthorized();
    }

    var unreadCounts = await db.ChatMessages
        .AsNoTracking()
        .Where(message => message.ReceiverId == userId && message.ReadAt == null)
        .GroupBy(message => message.SenderId)
        .Select(group => new { UserId = group.Key, Count = group.Count() })
        .ToDictionaryAsync(item => item.UserId, item => item.Count);

    var onlineAfter = DateTimeOffset.UtcNow.AddMinutes(-2);
    var companyId = CompanyAccess.GetCompanyId(principal);
    var users = await db.Users
        .AsNoTracking()
        .Where(user => user.Id != userId && user.CompanyId == companyId && user.IsActive)
        .OrderBy(user => user.DisplayName)
        .Select(user => new
        {
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Position,
            user.AvatarFileName,
            user.Role,
            user.LastSeenAt,
            IsOnline = user.LastSeenAt >= onlineAfter
        })
        .ToListAsync();

    return Results.Ok(users.Select(user => new ChatUserListItem(
        user.Id,
        user.UserName,
        user.DisplayName,
        user.Position,
        UserResponses.AvatarUrl(user.AvatarFileName),
        user.Role,
        user.LastSeenAt,
        user.IsOnline,
        unreadCounts.GetValueOrDefault(user.Id))));
}).RequireAuthorization();

app.MapGet("/api/chat/{userId:guid}/messages", async (
    Guid userId,
    AppDbContext db,
    ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Chats))
    {
        return Results.Forbid();
    }

    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(currentUserId, out var parsedCurrentUserId))
    {
        return Results.Unauthorized();
    }

    var companyId = CompanyAccess.GetCompanyId(principal);
    var chatUserExists = await db.Users.AnyAsync(user => user.Id == userId && user.CompanyId == companyId && user.IsActive);
    if (!chatUserExists)
    {
        return Results.NotFound();
    }

    var unreadMessages = await db.ChatMessages
        .Where(message =>
            message.SenderId == userId &&
            message.ReceiverId == parsedCurrentUserId &&
            message.ReadAt == null)
        .ToListAsync();

    if (unreadMessages.Count > 0)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var message in unreadMessages)
        {
            message.ReadAt = now;
        }

        await db.SaveChangesAsync();
    }

    var messages = await db.ChatMessages
        .AsNoTracking()
        .Where(message =>
            message.SenderId == parsedCurrentUserId && message.ReceiverId == userId ||
            message.SenderId == userId && message.ReceiverId == parsedCurrentUserId)
        .OrderBy(message => message.CreatedAt)
        .Select(message => new ChatMessageListItem(
            message.Id,
            message.SenderId,
            message.ReceiverId,
            message.Text,
            message.AttachmentFileName,
            message.AttachmentContentType,
            message.AttachmentContent != null,
            message.CreatedAt,
            message.SenderId == parsedCurrentUserId))
        .ToListAsync();

    return Results.Ok(messages);
}).RequireAuthorization();

app.MapPost("/api/chat/{userId:guid}/messages", async (
    Guid userId,
    HttpRequest request,
    AppDbContext db,
    ClaimsPrincipal principal,
    IHubContext<AppHub> hub,
    CancellationToken cancellationToken) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Chats))
    {
        return Results.Forbid();
    }

    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(currentUserId, out var parsedCurrentUserId))
    {
        return Results.Unauthorized();
    }

    if (parsedCurrentUserId == userId)
    {
        return Results.BadRequest("Нельзя отправить сообщение самому себе.");
    }

    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Ожидается multipart/form-data.");
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var text = form["text"].ToString().Trim();
    var file = form.Files.GetFile("file");

    if (text.Length > 2000)
    {
        return Results.BadRequest("Сообщение слишком длинное.");
    }

    if (file is not null && file.Length > 10 * 1024 * 1024)
    {
        return Results.BadRequest("Файл слишком большой. Максимум 10 МБ.");
    }

    if (string.IsNullOrWhiteSpace(text) && (file is null || file.Length == 0))
    {
        return Results.BadRequest("Напишите сообщение или прикрепите файл.");
    }

    var companyId = CompanyAccess.GetCompanyId(principal);
    var receiverExists = await db.Users.AnyAsync(user => user.Id == userId && user.CompanyId == companyId && user.IsActive);
    if (!receiverExists)
    {
        return Results.NotFound();
    }

    byte[]? attachmentContent = null;
    var attachmentFileName = string.Empty;
    var attachmentContentType = string.Empty;
    if (file is not null && file.Length > 0)
    {
        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        attachmentContent = memory.ToArray();
        attachmentFileName = Path.GetFileName(file.FileName);
        attachmentContentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;
    }

    var message = new ChatMessage
    {
        SenderId = parsedCurrentUserId,
        ReceiverId = userId,
        Text = text,
        AttachmentFileName = attachmentFileName,
        AttachmentContentType = attachmentContentType,
        AttachmentContent = attachmentContent
    };

    db.ChatMessages.Add(message);
    await db.SaveChangesAsync();

    var result = new ChatMessageListItem(
        message.Id,
        message.SenderId,
        message.ReceiverId,
        message.Text,
        message.AttachmentFileName,
        message.AttachmentContentType,
        message.AttachmentContent != null,
        message.CreatedAt,
        true);

    await hub.Clients.All.SendAsync("ChatMessagesChanged", message.SenderId, message.ReceiverId);

    return Results.Created($"/api/chat/{userId}/messages/{message.Id}", result);
}).DisableAntiforgery().RequireAuthorization();

app.MapGet("/api/chat/messages/{id:guid}/attachment", async (
    Guid id,
    AppDbContext db,
    ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Chats))
    {
        return Results.Forbid();
    }

    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(currentUserId, out var parsedCurrentUserId))
    {
        return Results.Unauthorized();
    }

    var message = await db.ChatMessages.AsNoTracking().FirstOrDefaultAsync(message => message.Id == id);
    if (message is null || message.AttachmentContent is null || string.IsNullOrWhiteSpace(message.AttachmentFileName))
    {
        return Results.NotFound();
    }

    var isAdmin = principal.IsInRole(UserRoles.Admin);
    if (message.SenderId != parsedCurrentUserId && message.ReceiverId != parsedCurrentUserId && !isAdmin)
    {
        return Results.Forbid();
    }

    return Results.File(message.AttachmentContent, message.AttachmentContentType, message.AttachmentFileName);
}).RequireAuthorization();

app.MapDelete("/api/chat/messages/{id:guid}", async (
    Guid id,
    AppDbContext db,
    ClaimsPrincipal principal,
    IHubContext<AppHub> hub) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Chats))
    {
        return Results.Forbid();
    }

    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(currentUserId, out var parsedCurrentUserId))
    {
        return Results.Unauthorized();
    }

    var message = await db.ChatMessages.FindAsync(id);
    if (message is null)
    {
        return Results.NotFound();
    }

    var isAdmin = principal.IsInRole(UserRoles.Admin);
    if (message.SenderId != parsedCurrentUserId && !isAdmin)
    {
        return Results.Forbid();
    }

    db.ChatMessages.Remove(message);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("ChatMessagesChanged", message.SenderId, message.ReceiverId);

    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/products", async (AppDbContext db, ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Products))
    {
        return Results.Forbid();
    }

    return Results.Ok(products);
})
    .WithName("GetProducts")
    .RequireAuthorization();

app.MapGet("/api/ozon/products", async (
    OzonApiClient ozonApi,
    Microsoft.Extensions.Options.IOptions<OzonOptions> options,
    AppDbContext db,
    ClaimsPrincipal principal,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Products, FeatureAccess.Production, FeatureAccess.Supplies))
    {
        return Results.Forbid();
    }

    try
    {
        await CompanyAccess.UseCompanyOzonCredentialsAsync(db, principal, dataProtectionProvider, options.Value, ozonApi);
        var result = await ozonApi.GetProductSummariesAsync(100, cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
    {
        return Results.Problem(exception.Message);
    }
}).RequireAuthorization();

app.MapGet("/api/product-supplier-links", async (AppDbContext db, ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Products, FeatureAccess.Supplies, FeatureAccess.Production))
    {
        return Results.Forbid();
    }

    var companyId = CompanyAccess.GetCompanyId(principal);
    var links = await db.ProductSupplierLinks
        .AsNoTracking()
        .Where(link => link.CompanyId == companyId)
        .OrderBy(link => link.SupplierName)
        .Select(link => new ProductSupplierLinkResponse(link.Id, link.OzonProductId, link.OfferId, link.SupplierName, link.SupplierUrl))
        .ToListAsync();
    return Results.Ok(links);
}).RequireAuthorization();

app.MapPut("/api/product-supplier-links/{ozonProductId:long}", async (
    long ozonProductId,
    UpdateProductSupplierLinksRequest request,
    AppDbContext db,
    ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Products, FeatureAccess.Supplies, FeatureAccess.Production))
    {
        return Results.Forbid();
    }

    var companyId = CompanyAccess.GetCompanyId(principal);
    if (request.Suppliers.Count > 20)
    {
        return Results.BadRequest("Для одного товара можно добавить не больше 20 поставщиков.");
    }

    var suppliers = request.Suppliers
        .Select(item => new UpdateProductSupplierLinkRequest(item.SupplierName.Trim(), item.SupplierUrl.Trim()))
        .Where(item => !string.IsNullOrWhiteSpace(item.SupplierName) && !string.IsNullOrWhiteSpace(item.SupplierUrl))
        .ToList();
    if (request.Suppliers.Count > 0 && suppliers.Count != request.Suppliers.Count)
    {
        return Results.BadRequest("У каждого поставщика должны быть название и ссылка.");
    }

    var currentLinks = await db.ProductSupplierLinks
        .Where(item => item.CompanyId == companyId && item.OzonProductId == ozonProductId)
        .ToListAsync();
    db.ProductSupplierLinks.RemoveRange(currentLinks);
    db.ProductSupplierLinks.AddRange(suppliers.Select(item => new ProductSupplierLink
    {
        CompanyId = companyId,
        OzonProductId = ozonProductId,
        OfferId = request.OfferId.Trim(),
        SupplierName = item.SupplierName,
        SupplierUrl = item.SupplierUrl,
        UpdatedAt = DateTimeOffset.UtcNow
    }));

    await db.SaveChangesAsync();
    var updated = await db.ProductSupplierLinks
        .AsNoTracking()
        .Where(item => item.CompanyId == companyId && item.OzonProductId == ozonProductId)
        .OrderBy(item => item.SupplierName)
        .Select(item => new ProductSupplierLinkResponse(item.Id, item.OzonProductId, item.OfferId, item.SupplierName, item.SupplierUrl))
        .ToListAsync();
    return Results.Ok(updated);
}).RequireAuthorization();

app.MapDelete("/api/product-supplier-links/{ozonProductId:long}", async (
    long ozonProductId,
    AppDbContext db,
    ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Products, FeatureAccess.Supplies, FeatureAccess.Production))
    {
        return Results.Forbid();
    }

    var companyId = CompanyAccess.GetCompanyId(principal);
    var links = await db.ProductSupplierLinks
        .Where(item => item.CompanyId == companyId && item.OzonProductId == ozonProductId)
        .ToListAsync();
    if (links.Count == 0)
    {
        return Results.NotFound();
    }

    db.ProductSupplierLinks.RemoveRange(links);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/ozon/stocks", async (
    OzonApiClient ozonApi,
    Microsoft.Extensions.Options.IOptions<OzonOptions> options,
    AppDbContext db,
    ClaimsPrincipal principal,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Pooling))
    {
        return Results.Forbid();
    }

    try
    {
        await CompanyAccess.UseCompanyOzonCredentialsAsync(db, principal, dataProtectionProvider, options.Value, ozonApi);
        var result = await ozonApi.GetStockSummariesAsync(100, cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
    {
        return Results.Problem(exception.Message);
    }
}).RequireAuthorization();

app.MapPut("/api/ozon/prices", async (
    OzonPriceUpdateRequest request,
    OzonApiClient ozonApi,
    Microsoft.Extensions.Options.IOptions<OzonOptions> options,
    AppDbContext db,
    ClaimsPrincipal principal,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, "pooling.editPrices"))
    {
        return Results.Forbid();
    }

    try
    {
        await CompanyAccess.UseCompanyOzonCredentialsAsync(db, principal, dataProtectionProvider, options.Value, ozonApi);
        var result = await ozonApi.UpdatePriceAsync(request, cancellationToken);
        AuditLogWriter.Add(
            db,
            principal,
            result.Success ? "Изменение цены Ozon" : "Ошибка изменения цены Ozon",
            "OzonProduct",
            request.ProductId.ToString(),
            $"{request.OfferId}: {request.Price} {request.CurrencyCode}. {result.Message}");
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
    {
        return Results.Problem(exception.Message);
    }
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapGet("/api/ozon/analytics", async (
    OzonApiClient ozonApi,
    Microsoft.Extensions.Options.IOptions<OzonOptions> options,
    AppDbContext db,
    ClaimsPrincipal principal,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Analytics, FeatureAccess.Pooling))
    {
        return Results.Forbid();
    }

    try
    {
        await CompanyAccess.UseCompanyOzonCredentialsAsync(db, principal, dataProtectionProvider, options.Value, ozonApi);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await ozonApi.GetAnalyticsAsync(today.AddDays(-27), today, cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
    {
        return Results.Problem(exception.Message);
    }
}).RequireAuthorization();

app.MapGet("/api/ozon/supply-orders", async (
    OzonApiClient ozonApi,
    Microsoft.Extensions.Options.IOptions<OzonOptions> options,
    AppDbContext db,
    ClaimsPrincipal principal,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Supplies))
    {
        return Results.Forbid();
    }

    try
    {
        await CompanyAccess.UseCompanyOzonCredentialsAsync(db, principal, dataProtectionProvider, options.Value, ozonApi);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Results.Ok(await ozonApi.GetSupplyOrdersAsync(today.AddDays(-60), today, cancellationToken));
    }
    catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
    {
        return Results.Problem(exception.Message);
    }
}).RequireAuthorization();

app.MapGet("/api/production/files", async (string? search, AppDbContext db, ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Production))
    {
        return Results.Forbid();
    }

    var query = db.ProductionFiles.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var value = search.Trim().ToLower();
        query = query.Where(file =>
            file.OfferId.ToLower().Contains(value) ||
            file.ProductName.ToLower().Contains(value) ||
            file.Notes.ToLower().Contains(value));
    }

    var files = await query
        .OrderByDescending(file => file.CreatedAt)
        .Select(file => new ProductionFileListItem(
            file.Id,
            file.OzonProductId,
            file.OfferId,
            file.ProductName,
            file.Notes,
            file.FileName,
            file.ContentType,
            file.CreatedAt))
        .ToListAsync();

    return Results.Ok(files);
}).RequireAuthorization();

app.MapPost("/api/production/files", async (
    HttpRequest request,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Ожидается multipart/form-data.");
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("Файл обязателен.");
    }

    await using var stream = file.OpenReadStream();
    using var memory = new MemoryStream();
    await stream.CopyToAsync(memory, cancellationToken);

    var productionFile = new ProductionFile
    {
        OzonProductId = long.TryParse(form["ozonProductId"], out var productId) ? productId : null,
        OfferId = form["offerId"].ToString().Trim(),
        ProductName = form["productName"].ToString().Trim(),
        Notes = form["notes"].ToString().Trim(),
        FileName = Path.GetFileName(file.FileName),
        ContentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType,
        Content = memory.ToArray()
    };

    db.ProductionFiles.Add(productionFile);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/production/files/{productionFile.Id}", new ProductionFileListItem(
        productionFile.Id,
        productionFile.OzonProductId,
        productionFile.OfferId,
        productionFile.ProductName,
        productionFile.Notes,
        productionFile.FileName,
        productionFile.ContentType,
        productionFile.CreatedAt));
}).DisableAntiforgery().RequireAuthorization();

app.MapGet("/api/production/files/{id:guid}/download", async (Guid id, AppDbContext db) =>
{
    var file = await db.ProductionFiles.FindAsync(id);
    if (file is null)
    {
        return Results.NotFound();
    }

    return Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization();

app.MapDelete("/api/production/files/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var file = await db.ProductionFiles.FindAsync(id);
    if (file is null)
    {
        return Results.NotFound();
    }

    db.ProductionFiles.Remove(file);
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapGet("/api/production/tasks", async (string? status, AppDbContext db, ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Production))
    {
        return Results.Forbid();
    }

    IQueryable<ProductionTask> query = db.ProductionTasks
        .AsNoTracking()
        .Include(task => task.Items);

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(task => task.Status == status);
    }

    var tasks = await query
        .OrderByDescending(task => task.CreatedAt)
        .Select(task => new ProductionTaskListItem(
            task.Id,
            task.OzonProductId,
            task.OfferId,
            task.ProductName,
            task.RequiredQuantity,
            task.ActualQuantity,
            task.Status,
            task.AssignedUserName,
            task.CreatedAt,
            task.StartedAt,
            task.DeferredAt,
            task.CompletedAt,
            task.IsArchived,
            task.ArchivedAt,
            task.Items.Count == 0
                ? new List<ProductionTaskItemListItem>
                {
                    new(task.Id, task.OzonProductId, task.OfferId, task.ProductName, task.RequiredQuantity, task.ActualQuantity)
                }
                : task.Items
                    .OrderBy(item => item.ProductName)
                    .Select(item => new ProductionTaskItemListItem(
                        item.Id,
                        item.OzonProductId,
                        item.OfferId,
                        item.ProductName,
                        item.RequiredQuantity,
                        item.ActualQuantity))
                    .ToList()))
        .ToListAsync();

    return Results.Ok(tasks);
}).RequireAuthorization();

app.MapGet("/api/production/tasks/archive/export", async (AppDbContext db) =>
{
    var tasks = await db.ProductionTasks
        .AsNoTracking()
        .Include(task => task.Items)
        .Where(task => task.IsArchived)
        .OrderByDescending(task => task.CompletedAt ?? task.CreatedAt)
        .ToListAsync();

    var builder = new StringBuilder();
    builder.AppendLine("ID задачи;Создана;Взята в работу;Завершена;Архивирована;Исполнитель;Статус;Товар;Артикул;План;Факт");

    foreach (var task in tasks)
    {
        var items = task.Items.Count == 0
            ? [new ProductionTaskItem
            {
                OzonProductId = task.OzonProductId,
                OfferId = task.OfferId,
                ProductName = task.ProductName,
                RequiredQuantity = task.RequiredQuantity,
                ActualQuantity = task.ActualQuantity
            }]
            : task.Items.OrderBy(item => item.ProductName).ToList();

        foreach (var item in items)
        {
            builder.AppendLine(string.Join(';', [
                CsvExport.Cell(task.Id.ToString()),
                CsvExport.Cell(task.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                CsvExport.Cell(task.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty),
                CsvExport.Cell(task.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty),
                CsvExport.Cell(task.ArchivedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty),
                CsvExport.Cell(task.AssignedUserName ?? string.Empty),
                CsvExport.Cell(task.Status),
                CsvExport.Cell(item.ProductName),
                CsvExport.Cell(item.OfferId),
                CsvExport.Cell(item.RequiredQuantity.ToString()),
                CsvExport.Cell((item.ActualQuantity ?? 0).ToString())
            ]));
        }
    }

    return Results.File(
        Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray(),
        "text/csv; charset=utf-8",
        $"production-task-archive-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapPost("/api/production/tasks", async (
    CreateProductionTaskRequest request,
    AppDbContext db,
    ClaimsPrincipal principal,
    IHubContext<AppHub> hub) =>
{
    var requestItems = request.Items is { Count: > 0 }
        ? request.Items
        : [new CreateProductionTaskItemRequest(
            request.OzonProductId,
            request.OfferId,
            request.ProductName,
            request.RequiredQuantity)];

    if (requestItems.Any(item => item.OzonProductId <= 0 || item.RequiredQuantity <= 0))
    {
        return Results.BadRequest("Выберите товар и укажите количество больше нуля.");
    }

    var firstItem = requestItems[0];
    var task = new ProductionTask
    {
        OzonProductId = firstItem.OzonProductId,
        OfferId = firstItem.OfferId.Trim(),
        ProductName = requestItems.Count == 1
            ? firstItem.ProductName.Trim()
            : $"Задача на {requestItems.Count} товаров",
        RequiredQuantity = requestItems.Sum(item => item.RequiredQuantity),
        Items = requestItems.Select(item => new ProductionTaskItem
        {
            OzonProductId = item.OzonProductId,
            OfferId = item.OfferId.Trim(),
            ProductName = item.ProductName.Trim(),
            RequiredQuantity = item.RequiredQuantity
        }).ToList()
    };

    db.ProductionTasks.Add(task);
    AuditLogWriter.Add(db, principal, "Создание задачи", "ProductionTask", task.Id.ToString(), task.ProductName);
    await db.SaveChangesAsync();

    var result = new ProductionTaskListItem(
        task.Id,
        task.OzonProductId,
        task.OfferId,
        task.ProductName,
        task.RequiredQuantity,
        task.ActualQuantity,
        task.Status,
        task.AssignedUserName,
        task.CreatedAt,
        task.StartedAt,
        task.DeferredAt,
        task.CompletedAt,
        task.IsArchived,
        task.ArchivedAt,
        task.Items.Select(item => new ProductionTaskItemListItem(
            item.Id,
            item.OzonProductId,
            item.OfferId,
            item.ProductName,
            item.RequiredQuantity,
            item.ActualQuantity)).ToList());

    await hub.Clients.All.SendAsync("ProductionTasksChanged", result);

    return Results.Created($"/api/production/tasks/{task.Id}", result);
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapPut("/api/production/tasks/{id:guid}/start", async (
    Guid id,
    AppDbContext db,
    ClaimsPrincipal principal,
    IHubContext<AppHub> hub) =>
{
    var task = await db.ProductionTasks.FindAsync(id);
    if (task is null)
    {
        return Results.NotFound();
    }

    if (task.Status == ProductionTaskStatuses.Completed)
    {
        return Results.BadRequest("Выполненную задачу нельзя взять в работу.");
    }

    task.Status = ProductionTaskStatuses.InProgress;
    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    var currentUser = Guid.TryParse(currentUserId, out var parsedUserId)
        ? await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == parsedUserId)
        : null;
    task.AssignedUserName = currentUser?.DisplayName
        ?? principal.FindFirstValue("display_name")
        ?? principal.FindFirstValue(ClaimTypes.Name)
        ?? task.AssignedUserName;
    task.StartedAt ??= DateTimeOffset.UtcNow;
    AuditLogWriter.Add(db, principal, "Задача взята в работу", "ProductionTask", task.Id.ToString(), task.ProductName);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("ProductionTasksChanged");

    return Results.NoContent();
}).RequireAuthorization();

app.MapPut("/api/production/tasks/{id:guid}/defer", async (
    Guid id,
    AppDbContext db,
    ClaimsPrincipal principal,
    IHubContext<AppHub> hub) =>
{
    var task = await db.ProductionTasks.FindAsync(id);
    if (task is null)
    {
        return Results.NotFound();
    }

    if (task.Status == ProductionTaskStatuses.Completed)
    {
        return Results.BadRequest("Выполненную задачу нельзя отложить.");
    }

    task.Status = ProductionTaskStatuses.Deferred;
    task.DeferredAt = DateTimeOffset.UtcNow;
    AuditLogWriter.Add(db, principal, "Задача отложена", "ProductionTask", task.Id.ToString(), task.ProductName);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("ProductionTasksChanged");

    return Results.NoContent();
}).RequireAuthorization();

app.MapPut("/api/production/tasks/{id:guid}/complete", async (
    Guid id,
    CompleteProductionTaskRequest request,
    AppDbContext db,
    ClaimsPrincipal principal,
    IHubContext<AppHub> hub) =>
{
    if (request.ActualQuantity < 0 || request.Items?.Any(item => item.ActualQuantity < 0) == true)
    {
        return Results.BadRequest("Фактическое количество не может быть меньше нуля.");
    }

    var task = await db.ProductionTasks
        .Include(task => task.Items)
        .FirstOrDefaultAsync(task => task.Id == id);
    if (task is null)
    {
        return Results.NotFound();
    }

    if (request.Items is { Count: > 0 })
    {
        var taskItems = task.Items.ToDictionary(item => item.Id);
        foreach (var requestItem in request.Items)
        {
            if (!taskItems.TryGetValue(requestItem.Id, out var taskItem))
            {
                return Results.BadRequest("В задаче есть неизвестный товар.");
            }

            taskItem.ActualQuantity = requestItem.ActualQuantity;
        }

        task.ActualQuantity = task.Items.Sum(item => item.ActualQuantity ?? 0);
    }
    else
    {
        task.ActualQuantity = request.ActualQuantity;
        if (task.Items.Count == 1)
        {
            task.Items[0].ActualQuantity = request.ActualQuantity;
        }
    }

    task.Status = ProductionTaskStatuses.Completed;
    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    var currentUser = Guid.TryParse(currentUserId, out var parsedUserId)
        ? await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == parsedUserId)
        : null;
    task.AssignedUserName ??= currentUser?.DisplayName ?? principal.FindFirstValue("display_name") ?? principal.FindFirstValue(ClaimTypes.Name);
    task.CompletedAt = DateTimeOffset.UtcNow;
    AuditLogWriter.Add(db, principal, "Задача завершена", "ProductionTask", task.Id.ToString(), $"{task.ProductName}. Факт: {task.ActualQuantity}");
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("ProductionTasksChanged");

    return Results.NoContent();
}).RequireAuthorization();

app.MapPut("/api/production/tasks/{id:guid}/archive", async (
    Guid id,
    AppDbContext db,
    ClaimsPrincipal principal,
    IHubContext<AppHub> hub) =>
{
    var task = await db.ProductionTasks.FindAsync(id);
    if (task is null)
    {
        return Results.NotFound();
    }

    if (task.Status != ProductionTaskStatuses.Completed)
    {
        return Results.BadRequest("В архив можно отправить только выполненную задачу.");
    }

    task.IsArchived = true;
    task.ArchivedAt = DateTimeOffset.UtcNow;
    AuditLogWriter.Add(db, principal, "Задача архивирована", "ProductionTask", task.Id.ToString(), task.ProductName);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("ProductionTasksChanged");

    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapDelete("/api/production/tasks/{id:guid}", async (
    Guid id,
    AppDbContext db,
    ClaimsPrincipal principal,
    IHubContext<AppHub> hub) =>
{
    var task = await db.ProductionTasks.FindAsync(id);
    if (task is null)
    {
        return Results.NotFound();
    }

    if (!task.IsArchived)
    {
        return Results.BadRequest("Удалить задачу можно только из архива.");
    }

    db.ProductionTasks.Remove(task);
    AuditLogWriter.Add(db, principal, "Удаление задачи", "ProductionTask", task.Id.ToString(), task.ProductName);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("ProductionTasksChanged");

    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapGet("/api/supplies", async (AppDbContext db, ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Supplies))
    {
        return Results.Forbid();
    }

    var supplies = await db.Supplies
        .AsNoTracking()
        .Include(supply => supply.Items)
        .OrderByDescending(supply => supply.CreatedAt)
        .ToListAsync();

    var supplyIds = supplies.Select(supply => supply.Id.ToString()).ToList();
    var histories = await db.AuditLogs
        .AsNoTracking()
        .Where(log => log.EntityType == "Supply" && supplyIds.Contains(log.EntityId))
        .OrderByDescending(log => log.CreatedAt)
        .Select(log => new
        {
            log.EntityId,
            Item = new SupplyHistoryItem(
                log.Id,
                log.UserName,
                log.DisplayName,
                log.Action,
                log.Details,
                log.CreatedAt)
        })
        .ToListAsync();

    var historiesBySupplyId = histories
        .GroupBy(log => log.EntityId)
        .ToDictionary(group => group.Key, group => group.Select(log => log.Item).ToList());

    return Results.Ok(supplies
        .Select(supply => new SupplyListItem(
            supply.Id,
            supply.Status,
            supply.CreatedAt,
            supply.SentAt,
            supply.AcceptedAt,
            supply.IsArchived,
            supply.ArchivedAt,
            supply.Items
                .OrderBy(item => item.ProductName)
                .Select(item => new SupplyItemListItem(
                    item.Id,
                    item.OzonProductId,
                    item.OfferId,
                    item.ProductName,
                    item.Quantity,
                    item.ActualOrderQuantity,
                    item.IsReserve))
                .ToList(),
            historiesBySupplyId.GetValueOrDefault(supply.Id.ToString()) ?? []))
        .ToList());
}).RequireAuthorization();

app.MapPost("/api/supplies", async (CreateSupplyRequest request, AppDbContext db, ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Supplies))
    {
        return Results.Forbid();
    }

    if (request.Items.Count == 0)
    {
        return Results.BadRequest("Добавьте хотя бы один товар в поставку.");
    }

    var supply = new Supply
    {
        Status = SupplyStatuses.Created,
        Items = request.Items.Select(item => new SupplyItem
        {
            OzonProductId = item.IsReserve ? null : item.OzonProductId,
            OfferId = item.IsReserve ? string.Empty : item.OfferId.Trim(),
            ProductName = item.ProductName.Trim(),
            Quantity = item.Quantity,
            ActualOrderQuantity = item.ActualOrderQuantity ?? item.Quantity,
            IsReserve = item.IsReserve
        }).ToList()
    };

    if (supply.Items.Any(item => item.Quantity <= 0 || item.ActualOrderQuantity < 0 || string.IsNullOrWhiteSpace(item.ProductName)))
    {
        return Results.BadRequest("Укажите название, количество больше нуля и факт заказа не меньше нуля для каждой строки.");
    }

    db.Supplies.Add(supply);
    AuditLogWriter.Add(db, principal, "Создание поставки", "Supply", supply.Id.ToString(), $"Товаров: {supply.Items.Count}");
    await db.SaveChangesAsync();

    return Results.Created($"/api/supplies/{supply.Id}", new SupplyListItem(
        supply.Id,
        supply.Status,
        supply.CreatedAt,
        supply.SentAt,
        supply.AcceptedAt,
        supply.IsArchived,
        supply.ArchivedAt,
        supply.Items.Select(item => new SupplyItemListItem(
            item.Id,
            item.OzonProductId,
            item.OfferId,
            item.ProductName,
            item.Quantity,
            item.ActualOrderQuantity,
            item.IsReserve)).ToList(),
        []));
}).RequireAuthorization();

app.MapGet("/api/supplies/import-template", () =>
{
    var content = ExcelSupplyImport.CreateTemplate();
    return Results.File(
        content,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "supply-template.xlsx");
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapPost("/api/supplies/import", async (
    HttpRequest request,
    AppDbContext db,
    ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Ожидается multipart/form-data.");
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("Выберите Excel-файл.");
    }

    await using var stream = file.OpenReadStream();
    List<CreateSupplyItemRequest> importedItems;
    try
    {
        importedItems = ExcelSupplyImport.ReadSupplyItems(stream);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(exception.Message);
    }

    if (importedItems.Count == 0)
    {
        return Results.BadRequest("В файле нет строк для импорта.");
    }

    var supply = new Supply
    {
        Status = SupplyStatuses.Created,
        Items = importedItems.Select(item => new SupplyItem
        {
            OzonProductId = item.IsReserve ? null : item.OzonProductId,
            OfferId = item.IsReserve ? string.Empty : item.OfferId.Trim(),
            ProductName = item.ProductName.Trim(),
            Quantity = item.Quantity,
            ActualOrderQuantity = item.ActualOrderQuantity ?? item.Quantity,
            IsReserve = item.IsReserve
        }).ToList()
    };

    if (supply.Items.Any(item => item.Quantity <= 0 || string.IsNullOrWhiteSpace(item.ProductName)))
    {
        return Results.BadRequest("Проверьте название и количество в Excel-файле.");
    }

    db.Supplies.Add(supply);
    AuditLogWriter.Add(db, principal, "Импорт поставки из Excel", "Supply", supply.Id.ToString(), $"Товаров: {supply.Items.Count}");
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(new { supply.Id, Items = supply.Items.Count });
}).DisableAntiforgery().RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapPut("/api/supplies/{id:guid}/status", async (
    Guid id,
    ChangeSupplyStatusRequest request,
    AppDbContext db,
    ClaimsPrincipal principal) =>
{
    var supply = await db.Supplies.FindAsync(id);
    if (supply is null)
    {
        return Results.NotFound();
    }

    if (supply.IsArchived)
    {
        return Results.BadRequest("Архивную поставку нельзя менять.");
    }

    var now = DateTimeOffset.UtcNow;
    if (request.Status == SupplyStatuses.Sent)
    {
        if (supply.Status != SupplyStatuses.Created)
        {
            return Results.BadRequest("Отправить можно только поставку в статусе создано.");
        }

        supply.Status = SupplyStatuses.Sent;
        supply.SentAt ??= now;
    }
    else if (request.Status == SupplyStatuses.Accepted)
    {
        if (!principal.IsInRole(UserRoles.Admin))
        {
            return Results.Forbid();
        }

        supply.Status = SupplyStatuses.Accepted;
        supply.AcceptedAt ??= now;
    }
    else
    {
        return Results.BadRequest("Можно поставить только статус отправлено или принято.");
    }

    AuditLogWriter.Add(db, principal, $"Статус поставки: {request.Status}", "Supply", supply.Id.ToString(), supply.Status);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapPut("/api/supplies/{id:guid}", async (
    Guid id,
    UpdateSupplyRequest request,
    AppDbContext db,
    ClaimsPrincipal principal) =>
{
    var supply = await db.Supplies
        .Include(item => item.Items)
        .SingleOrDefaultAsync(item => item.Id == id);
    if (supply is null)
    {
        return Results.NotFound();
    }

    if (supply.IsArchived)
    {
        return Results.BadRequest("Архивную поставку нельзя редактировать.");
    }

    var isAdmin = principal.IsInRole(UserRoles.Admin);
    if (!isAdmin && supply.Status != SupplyStatuses.Created)
    {
        return Results.Forbid();
    }

    if (request.Items.Count == 0)
    {
        return Results.BadRequest("В поставке должен быть хотя бы один товар.");
    }

    var updatedItems = request.Items.Select(item => new SupplyItem
    {
        SupplyId = supply.Id,
        OzonProductId = item.IsReserve ? null : item.OzonProductId,
        OfferId = item.IsReserve ? string.Empty : item.OfferId.Trim(),
        ProductName = item.ProductName.Trim(),
        Quantity = item.Quantity,
        ActualOrderQuantity = item.ActualOrderQuantity ?? item.Quantity,
        IsReserve = item.IsReserve
    }).ToList();

    if (updatedItems.Any(item => item.Quantity <= 0 || item.ActualOrderQuantity < 0 || string.IsNullOrWhiteSpace(item.ProductName)))
    {
        return Results.BadRequest("Укажите название, количество больше нуля и факт заказа не меньше нуля для каждой строки.");
    }

    db.SupplyItems.RemoveRange(supply.Items);
    db.SupplyItems.AddRange(updatedItems);
    AuditLogWriter.Add(db, principal, "Редактирование поставки", "Supply", supply.Id.ToString(), $"Товаров: {updatedItems.Count}");
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization();

app.MapPut("/api/supply-items/{id:guid}/actual-order", async (
    Guid id,
    UpdateSupplyItemActualOrderRequest request,
    AppDbContext db,
    ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Supplies))
    {
        return Results.Forbid();
    }

    if (request.ActualOrderQuantity < 0)
    {
        return Results.BadRequest("Факт заказа не может быть меньше нуля.");
    }

    var item = await db.SupplyItems
        .Include(supplyItem => supplyItem.Supply)
        .SingleOrDefaultAsync(supplyItem => supplyItem.Id == id);
    if (item is null)
    {
        return Results.NotFound();
    }

    if (item.Supply.IsArchived)
    {
        return Results.BadRequest("Архивную поставку нельзя менять.");
    }

    item.ActualOrderQuantity = request.ActualOrderQuantity;
    AuditLogWriter.Add(db, principal, "Факт заказа поставки", "SupplyItem", item.Id.ToString(), $"{item.ProductName}: {item.ActualOrderQuantity}");
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapPut("/api/supplies/{id:guid}/archive", async (Guid id, AppDbContext db, ClaimsPrincipal principal) =>
{
    var supply = await db.Supplies.FindAsync(id);
    if (supply is null)
    {
        return Results.NotFound();
    }

    supply.IsArchived = true;
    supply.ArchivedAt = DateTimeOffset.UtcNow;
    AuditLogWriter.Add(db, principal, "Поставка архивирована", "Supply", supply.Id.ToString(), supply.Status);
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapDelete("/api/supplies/{id:guid}", async (Guid id, AppDbContext db, ClaimsPrincipal principal) =>
{
    var supply = await db.Supplies.FindAsync(id);
    if (supply is null)
    {
        return Results.NotFound();
    }

    if (!supply.IsArchived)
    {
        return Results.BadRequest("Удалить поставку можно только из архива.");
    }

    db.Supplies.Remove(supply);
    AuditLogWriter.Add(db, principal, "Удаление поставки", "Supply", supply.Id.ToString(), supply.Status);
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapPut("/api/supplies/items/{id:guid}/replace-reserve", async (
    Guid id,
    ReplaceReserveSupplyItemRequest request,
    AppDbContext db,
    ClaimsPrincipal principal) =>
{
    var item = await db.SupplyItems.FindAsync(id);
    if (item is null)
    {
        return Results.NotFound();
    }

    if (!item.IsReserve)
    {
        return Results.BadRequest("Эта строка уже привязана к постоянному товару.");
    }

    if (request.OzonProductId <= 0 || string.IsNullOrWhiteSpace(request.ProductName))
    {
        return Results.BadRequest("Выберите постоянный товар.");
    }

    item.OzonProductId = request.OzonProductId;
    item.OfferId = request.OfferId.Trim();
    item.ProductName = request.ProductName.Trim();
    item.IsReserve = false;
    AuditLogWriter.Add(db, principal, "Замена резервного товара", "SupplyItem", item.Id.ToString(), item.ProductName);
    AuditLogWriter.Add(db, principal, "Замена резервного товара", "Supply", item.SupplyId.ToString(), item.ProductName);
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapGet("/api/supplies/analytics", async (AppDbContext db, ClaimsPrincipal principal) =>
{
    if (!await FeatureAccess.HasAnyAsync(db, principal, FeatureAccess.Supplies))
    {
        return Results.Forbid();
    }

    var items = await db.SupplyItems
        .AsNoTracking()
        .Include(item => item.Supply)
        .ToListAsync();

    return Results.Ok(items
        .GroupBy(item => new
        {
            item.SupplyId,
            ProductKey = item.OzonProductId.HasValue
                ? item.OzonProductId.Value.ToString()
                : item.OfferId != string.Empty
                    ? item.OfferId
                    : item.ProductName.ToLower(),
            item.OzonProductId,
            item.OfferId,
            item.ProductName,
            item.IsReserve,
            item.Supply.Status,
            item.Supply.CreatedAt,
            item.Supply.SentAt,
            item.Supply.AcceptedAt
        })
        .OrderByDescending(group => group.Key.CreatedAt)
        .Select(group => new SupplyAnalyticsItem(
            group.Min(item => item.Id),
            group.Key.SupplyId,
            group.Key.OzonProductId,
            group.Key.OfferId,
            group.Key.ProductName,
            group.Sum(item => item.Quantity),
            group.Key.IsReserve,
            group.Key.Status,
            group.Key.CreatedAt,
            group.Key.SentAt,
            group.Key.AcceptedAt))
        .ToList());
})
    .RequireAuthorization();

app.MapGet("/api/supplies/analytics/export", async (AppDbContext db) =>
{
    var items = await db.SupplyItems
        .AsNoTracking()
        .Include(item => item.Supply)
        .ToListAsync();

    var rows = items
        .GroupBy(item => new
        {
            item.SupplyId,
            item.OzonProductId,
            item.OfferId,
            item.ProductName,
            item.IsReserve,
            item.Supply.Status,
            item.Supply.CreatedAt,
            item.Supply.SentAt,
            item.Supply.AcceptedAt
        })
        .OrderByDescending(group => group.Key.CreatedAt)
        .ThenBy(group => group.Key.ProductName)
        .ToList();

    var builder = new StringBuilder();
    builder.AppendLine("Дата создания;Дата отправки;Дата приемки;Статус;Товар;Артикул;Количество;Резервный;ID поставки");
    foreach (var row in rows)
    {
        builder.AppendLine(string.Join(';', [
            CsvExport.Cell(row.Key.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
            CsvExport.Cell(row.Key.SentAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty),
            CsvExport.Cell(row.Key.AcceptedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty),
            CsvExport.Cell(row.Key.Status),
            CsvExport.Cell(row.Key.ProductName),
            CsvExport.Cell(row.Key.OfferId),
            CsvExport.Cell(row.Sum(item => item.Quantity).ToString()),
            CsvExport.Cell(row.Key.IsReserve ? "Да" : "Нет"),
            CsvExport.Cell(row.Key.SupplyId.ToString())
        ]));
    }

    return Results.File(
        Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray(),
        "text/csv; charset=utf-8",
        $"supplies-analytics-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapPost("/api/billing/checkout", async (
    HttpRequest currentRequest,
    AppDbContext db,
    ClaimsPrincipal principal,
    IHttpClientFactory httpClientFactory,
    Microsoft.Extensions.Options.IOptions<YooKassaOptions> options) =>
{
    var value = options.Value;
    if (string.IsNullOrWhiteSpace(value.ShopId) || string.IsNullOrWhiteSpace(value.SecretKey))
    {
        return Results.BadRequest("ЮKassa не настроена: задайте YooKassa__ShopId и YooKassa__SecretKey.");
    }

    var companyId = CompanyAccess.GetCompanyId(principal);
    var company = await db.Companies.FindAsync(companyId);
    if (company is null)
    {
        return Results.Unauthorized();
    }

    var returnUrl = string.IsNullOrWhiteSpace(value.ReturnUrl)
        ? $"{BillingPublicText.GetRequestOrigin(currentRequest)}/"
        : value.ReturnUrl;
    var request = new
    {
        amount = new
        {
            value = value.MonthlyAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            currency = value.Currency
        },
        confirmation = new
        {
            type = "redirect",
            return_url = returnUrl
        },
        capture = true,
        description = $"Подписка Fulvero на 1 месяц: {company.Name}",
        save_payment_method = true,
        metadata = new
        {
            companyId = company.Id.ToString()
        }
    };

    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v3/payments");
    httpRequest.Headers.Authorization = BillingPublicText.CreateBasicAuthHeader(value.ShopId, value.SecretKey);
    httpRequest.Headers.Add("Idempotence-Key", Guid.NewGuid().ToString());
    httpRequest.Content = System.Net.Http.Json.JsonContent.Create(request);

    using var response = await httpClientFactory.CreateClient("YooKassa").SendAsync(httpRequest);
    var content = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        return Results.Problem(content, statusCode: (int)response.StatusCode);
    }

    var payment = JsonSerializer.Deserialize<JsonElement>(content);
    var paymentId = payment.GetProperty("id").GetString() ?? string.Empty;
    var confirmationUrl = payment.GetProperty("confirmation").GetProperty("confirmation_url").GetString() ?? string.Empty;

    company.LastYooKassaPaymentId = paymentId;
    await db.SaveChangesAsync();

    return Results.Ok(new BillingCheckoutResponse(confirmationUrl, paymentId));
}).RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

app.MapPost("/api/billing/yookassa/webhook", async (
    HttpRequest request,
    AppDbContext db,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    var body = await new StreamReader(request.Body).ReadToEndAsync(cancellationToken);
    var payload = JsonSerializer.Deserialize<JsonElement>(body);
    if (!payload.TryGetProperty("event", out var eventElement)
        || eventElement.GetString() != "payment.succeeded"
        || !payload.TryGetProperty("object", out var payment))
    {
        return Results.Ok();
    }

    if (!payment.TryGetProperty("metadata", out var metadata)
        || !metadata.TryGetProperty("companyId", out var companyIdElement)
        || !Guid.TryParse(companyIdElement.GetString(), out var companyId))
    {
        return Results.Ok();
    }

    var company = await db.Companies.FindAsync([companyId], cancellationToken);
    if (company is null)
    {
        return Results.Ok();
    }

    company.SubscriptionStatus = CompanySubscriptionStatuses.Active;
    company.SubscriptionPaidUntil = DateTimeOffset.UtcNow.AddMonths(1);
    company.LastYooKassaPaymentId = payment.GetProperty("id").GetString() ?? company.LastYooKassaPaymentId;

    if (payment.TryGetProperty("payment_method", out var paymentMethod)
        && paymentMethod.TryGetProperty("saved", out var saved)
        && saved.ValueKind == JsonValueKind.True
        && paymentMethod.TryGetProperty("id", out var paymentMethodId))
    {
        var protector = dataProtectionProvider.CreateProtector("Fulvero.YooKassaPaymentMethod.v1");
        company.YooKassaPaymentMethodIdProtected = protector.Protect(paymentMethodId.GetString() ?? string.Empty);
    }

    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok();
});

if (hasStaticClient)
{
    app.MapFallbackToFile("index.html");
}

app.Run();

record Product(int Id, string Name, string Status, decimal Price);
record RegisterCompanyRequest(string CompanyName, string UserName, string DisplayName, string Password);
record LoginRequest(string CompanyName, string UserName, string Password);
record AuthResponse(string Token, CurrentUserResponse User);
record CurrentUserResponse(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string UserName,
    string DisplayName,
    string Position,
    string Role,
    string AvatarUrl,
    List<string> AllowedFeatures,
    string SubscriptionStatus,
    DateTimeOffset TrialEndsAt,
    DateTimeOffset? SubscriptionPaidUntil,
    bool HasActiveSubscription);
record CreateUserRequest(string UserName, string DisplayName, string Position, string Password, string Role, List<string>? AllowedFeatures);
record UpdateUserSettingsRequest(string DisplayName, string Position, string Role, List<string>? AllowedFeatures);
record UpdateProfileRequest(string DisplayName);
record ChangeUserPasswordRequest(string Password);
record UserListItem(
    Guid Id,
    string UserName,
    string DisplayName,
    string Position,
    string Role,
    string AvatarUrl,
    List<string> AllowedFeatures,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    bool IsOnline);
record ChatUserListItem(
    Guid Id,
    string UserName,
    string DisplayName,
    string Position,
    string AvatarUrl,
    string Role,
    DateTimeOffset? LastSeenAt,
    bool IsOnline,
    int UnreadCount);
record ChatMessageListItem(
    Guid Id,
    Guid SenderId,
    Guid ReceiverId,
    string Text,
    string AttachmentFileName,
    string AttachmentContentType,
    bool HasAttachment,
    DateTimeOffset CreatedAt,
    bool IsOwn);
record ProductionFileListItem(
    Guid Id,
    long? OzonProductId,
    string OfferId,
    string ProductName,
    string Notes,
    string FileName,
    string ContentType,
    DateTimeOffset CreatedAt);
record CreateProductionTaskRequest(long OzonProductId, string OfferId, string ProductName, int RequiredQuantity, List<CreateProductionTaskItemRequest>? Items);
record CreateProductionTaskItemRequest(long OzonProductId, string OfferId, string ProductName, int RequiredQuantity);
record CompleteProductionTaskRequest(int ActualQuantity, List<CompleteProductionTaskItemRequest>? Items);
record CompleteProductionTaskItemRequest(Guid Id, int ActualQuantity);
record ProductionTaskListItem(
    Guid Id,
    long OzonProductId,
    string OfferId,
    string ProductName,
    int RequiredQuantity,
    int? ActualQuantity,
    string Status,
    string? AssignedUserName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? DeferredAt,
    DateTimeOffset? CompletedAt,
    bool IsArchived,
    DateTimeOffset? ArchivedAt,
    List<ProductionTaskItemListItem> Items);
record ProductionTaskItemListItem(Guid Id, long OzonProductId, string OfferId, string ProductName, int RequiredQuantity, int? ActualQuantity);
record CreateSupplyRequest(List<CreateSupplyItemRequest> Items);
record CreateSupplyItemRequest(long? OzonProductId, string OfferId, string ProductName, int Quantity, int? ActualOrderQuantity, bool IsReserve);
record UpdateSupplyRequest(List<CreateSupplyItemRequest> Items);
record ChangeSupplyStatusRequest(string Status);
record ReplaceReserveSupplyItemRequest(long OzonProductId, string OfferId, string ProductName);
record UpdateSupplyItemActualOrderRequest(int ActualOrderQuantity);
record SupplyListItem(
    Guid Id,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? AcceptedAt,
    bool IsArchived,
    DateTimeOffset? ArchivedAt,
    List<SupplyItemListItem> Items,
    List<SupplyHistoryItem> History);
record SupplyItemListItem(
    Guid Id,
    long? OzonProductId,
    string OfferId,
    string ProductName,
    int Quantity,
    int ActualOrderQuantity,
    bool IsReserve);
record SupplyHistoryItem(
    Guid Id,
    string UserName,
    string DisplayName,
    string Action,
    string Details,
    DateTimeOffset CreatedAt);
record SupplyAnalyticsItem(
    Guid Id,
    Guid SupplyId,
    long? OzonProductId,
    string OfferId,
    string ProductName,
    int Quantity,
    bool IsReserve,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? AcceptedAt);
record AuditLogListItem(
    Guid Id,
    string UserName,
    string DisplayName,
    string Action,
    string EntityType,
    string EntityId,
    string Details,
    DateTimeOffset CreatedAt);
record SystemHealthResponse(
    bool DatabaseOk,
    DateTimeOffset ServerTime,
    string Uptime,
    string MachineName,
    string DotnetVersion);
record BackupFileResponse(string FileName, long SizeBytes, DateTimeOffset CreatedAt);
record BillingCheckoutResponse(string ConfirmationUrl, string PaymentId);
record ProductSupplierLinkResponse(Guid Id, long OzonProductId, string OfferId, string SupplierName, string SupplierUrl);
record UpdateProductSupplierLinksRequest(string OfferId, List<UpdateProductSupplierLinkRequest> Suppliers);
record UpdateProductSupplierLinkRequest(string SupplierName, string SupplierUrl);
record UpdateOzonIntegrationRequest(string ClientId, string ApiKey);
record OzonIntegrationStatusResponse(
    bool Configured,
    bool Success,
    string Message,
    string BaseUrl,
    string ClientIdMasked,
    string ApiKeyMasked,
    DateTimeOffset CheckedAt);
record OzonCredentials(string ClientId, string ApiKey);

static class AuditLogWriter
{
    public static void Add(
        AppDbContext db,
        ClaimsPrincipal principal,
        string action,
        string entityType,
        string entityId,
        string details)
    {
        Guid? userId = null;
        if (Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId))
        {
            userId = parsedUserId;
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            UserName = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            DisplayName = principal.FindFirstValue("display_name") ?? string.Empty,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
        });
    }
}

static class AppPublicText
{
    public static string MaskSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "не задан";
        }

        if (value.Length <= 6)
        {
            return new string('*', value.Length);
        }

        return $"{value[..3]}...{value[^3..]}";
    }

    public static string GetPublicOzonError(Exception exception)
    {
        var message = exception.Message;
        if (message.Length > 220)
        {
            message = $"{message[..220]}...";
        }

        return $"Ozon API не отвечает: {message}";
    }
}

static class CompanyAccess
{
    public static Guid GetCompanyId(ClaimsPrincipal principal)
    {
        var companyIdClaim = principal.FindFirstValue("company_id");
        if (!Guid.TryParse(companyIdClaim, out var companyId))
        {
            throw new InvalidOperationException("Company claim is missing.");
        }

        return companyId;
    }

    public static string NormalizeLoginName(string value) => value.Trim().ToLowerInvariant();

    public static async Task UseCompanyOzonCredentialsAsync(
        AppDbContext db,
        ClaimsPrincipal principal,
        IDataProtectionProvider dataProtectionProvider,
        OzonOptions fallbackOptions,
        OzonApiClient ozonApi)
    {
        var protector = dataProtectionProvider.CreateProtector("Fulvero.OzonCredentials.v1");
        var credentials = await GetOzonCredentialsAsync(db, principal, protector, fallbackOptions);
        if (!string.IsNullOrWhiteSpace(credentials.ClientId) && !string.IsNullOrWhiteSpace(credentials.ApiKey))
        {
            ozonApi.UseCredentials(credentials.ClientId, credentials.ApiKey);
        }
    }

    public static async Task<OzonCredentials> GetOzonCredentialsAsync(
        AppDbContext db,
        ClaimsPrincipal principal,
        IDataProtector protector,
        OzonOptions fallbackOptions)
    {
        var companyId = GetCompanyId(principal);
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(item => item.Id == companyId);
        if (company is null)
        {
            return new OzonCredentials(fallbackOptions.ClientId, fallbackOptions.ApiKey);
        }

        var clientId = UnprotectOrEmpty(protector, company.OzonClientIdProtected);
        var apiKey = UnprotectOrEmpty(protector, company.OzonApiKeyProtected);

        return string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey)
            ? new OzonCredentials(fallbackOptions.ClientId, fallbackOptions.ApiKey)
            : new OzonCredentials(clientId, apiKey);
    }

    private static string UnprotectOrEmpty(IDataProtector protector, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            return protector.Unprotect(value);
        }
        catch
        {
            return string.Empty;
        }
    }
}

static class SubscriptionAccess
{
    public static bool IsActive(Company company)
    {
        var now = DateTimeOffset.UtcNow;
        if (company.SubscriptionStatus == CompanySubscriptionStatuses.Active
            && company.SubscriptionPaidUntil is not null
            && company.SubscriptionPaidUntil > now)
        {
            return true;
        }

        return company.SubscriptionStatus == CompanySubscriptionStatuses.Trial
            && company.TrialEndsAt > now;
    }

    public static bool IsExemptPath(PathString path) =>
        path.StartsWithSegments("/api/auth")
        || path.StartsWithSegments("/api/billing")
        || path.StartsWithSegments("/api/avatars")
        || path.StartsWithSegments("/hubs/live");
}

static class BillingPublicText
{
    public static AuthenticationHeaderValue CreateBasicAuthHeader(string shopId, string secretKey)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{shopId}:{secretKey}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    public static string GetRequestOrigin(HttpRequest request)
    {
        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
        var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.Value;
        return $"{scheme}://{host}";
    }
}

static class FeatureAccess
{
    public const string Production = "production";
    public const string Products = "products";
    public const string Analytics = "analytics";
    public const string Pooling = "pooling";
    public const string Supplies = "supplies";
    public const string Chats = "chats";

    public static readonly string[] UserDefaults =
    [
        Production,
        "production.products",
        "production.tasks",
        "production.inProgress",
        "production.deferred",
        "production.completed",
        Products,
        Supplies,
        "supplies.create",
        "supplies.ozon",
        "supplies.all",
        Chats
    ];

    public static readonly string[] All =
    [
        Production,
        "production.products",
        "production.tasks",
        "production.inProgress",
        "production.deferred",
        "production.completed",
        "production.archive",
        "production.createTask",
        Products,
        Analytics,
        "analytics.summary",
        "analytics.topProducts",
        Pooling,
        "pooling.editPrices",
        Supplies,
        "supplies.create",
        "supplies.editor",
        "supplies.ozon",
        "supplies.all",
        "supplies.archive",
        "supplies.analytics",
        Chats
    ];

    public static List<string> Parse(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(feature => All.Contains(feature))
            .Distinct()
            .ToList();

    public static string NormalizeForRole(string role, IReadOnlyCollection<string>? features)
    {
        if (role == UserRoles.Admin)
        {
            return string.Join(',', All);
        }

        var selected = features is { Count: > 0 }
            ? features.Where(feature => All.Contains(feature)).Distinct().ToList()
            : UserDefaults.ToList();

        return string.Join(',', selected);
    }

    public static async Task<bool> HasAnyAsync(AppDbContext db, ClaimsPrincipal principal, params string[] features)
    {
        if (principal.IsInRole(UserRoles.Admin))
        {
            return true;
        }

        var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserId, out var userId))
        {
            return false;
        }

        var allowedFeatures = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.IsActive)
            .Select(user => user.AllowedFeatures)
            .FirstOrDefaultAsync();

        if (allowedFeatures is null)
        {
            return false;
        }

        var allowed = Parse(allowedFeatures);
        return features.Any(feature => allowed.Contains(feature));
    }
}

static class UserResponses
{
    public static CurrentUserResponse Current(AppUser user) =>
        new(
            user.Id,
            user.CompanyId,
            user.Company?.Name ?? string.Empty,
            user.UserName,
            user.DisplayName,
            user.Position,
            user.Role,
            AvatarUrl(user),
            Features(user),
            user.Company?.SubscriptionStatus ?? CompanySubscriptionStatuses.Trial,
            user.Company?.TrialEndsAt ?? DateTimeOffset.UtcNow,
            user.Company?.SubscriptionPaidUntil,
            user.Company is not null && SubscriptionAccess.IsActive(user.Company));

    public static List<string> Features(AppUser user) =>
        user.Role == UserRoles.Admin ? FeatureAccess.All.ToList() : FeatureAccess.Parse(user.AllowedFeatures);

    public static string AvatarUrl(AppUser user) => AvatarUrl(user.AvatarFileName);

    public static string AvatarUrl(string avatarFileName) =>
        string.IsNullOrWhiteSpace(avatarFileName) ? string.Empty : $"/api/avatars/{Uri.EscapeDataString(avatarFileName)}";
}

static class AppPaths
{
    public static string GetAvatarDirectory(IWebHostEnvironment environment) =>
        Path.GetFullPath(Path.Combine(environment.ContentRootPath, "user-avatars"));

    public static string GetBackupDirectory(IWebHostEnvironment environment)
    {
        var contentRootBackups = Path.Combine(environment.ContentRootPath, "backups");
        if (Directory.Exists(contentRootBackups))
        {
            return Path.GetFullPath(contentRootBackups);
        }

        var parent = Directory.GetParent(environment.ContentRootPath)?.FullName;
        return Path.GetFullPath(Path.Combine(parent ?? environment.ContentRootPath, "backups"));
    }
}

static class CsvExport
{
    public static string Cell(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}

static class ExcelSupplyImport
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] CreateTemplate()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            WriteEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            WriteEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Поставка" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);

            var rows = new[]
            {
                new[] { "Название товара", "Артикул", "ProductId", "Количество", "Резервный" },
                new[] { "Пример постоянного товара", "OFFER-001", "123456789", "10", "нет" },
                new[] { "Пример резервного товара", "", "", "5", "да" }
            };
            WriteEntry(archive, "xl/worksheets/sheet1.xml", CreateWorksheet(rows));
        }

        return memory.ToArray();
    }

    public static List<CreateSupplyItemRequest> ReadSupplyItems(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidOperationException("В Excel-файле не найден первый лист.");

        using var sheetStream = sheetEntry.Open();
        var sheet = XDocument.Load(sheetStream);
        var rows = sheet.Descendants(Spreadsheet + "row")
            .Skip(1)
            .Select(row => ReadRow(row, sharedStrings))
            .Where(values => values.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToList();

        return rows.Select((values, index) =>
        {
            var productName = GetValue(values, 0);
            var offerId = GetValue(values, 1);
            var productIdText = GetValue(values, 2);
            var quantityText = GetValue(values, 3);
            var reserveText = GetValue(values, 4);

            if (!int.TryParse(quantityText, out var quantity) || quantity <= 0)
            {
                throw new InvalidOperationException($"Строка {index + 2}: количество должно быть больше нуля.");
            }

            var isReserve = IsTrue(reserveText) || string.IsNullOrWhiteSpace(offerId);
            long? productId = long.TryParse(productIdText, out var parsedProductId) ? parsedProductId : null;

            if (!isReserve && string.IsNullOrWhiteSpace(offerId))
            {
                throw new InvalidOperationException($"Строка {index + 2}: для постоянного товара нужен артикул.");
            }

            return new CreateSupplyItemRequest(productId, offerId, productName, quantity, quantity, isReserve);
        }).ToList();
    }

    private static string CreateWorksheet(IReadOnlyList<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            builder.Append($"""<row r="{rowIndex + 1}">""");
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                var cellRef = $"{ColumnName(columnIndex)}{rowIndex + 1}";
                var value = System.Security.SecurityElement.Escape(rows[rowIndex][columnIndex]) ?? string.Empty;
                builder.Append($"""<c r="{cellRef}" t="inlineStr"><is><t>{value}</t></is></c>""");
            }
            builder.Append("</row>");
        }
        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants(Spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(Spreadsheet + "t").Select(text => text.Value)))
            .ToList();
    }

    private static List<string> ReadRow(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var values = new List<string>();
        foreach (var cell in row.Elements(Spreadsheet + "c"))
        {
            var reference = cell.Attribute("r")?.Value ?? string.Empty;
            var index = ColumnIndex(reference);
            while (values.Count <= index)
            {
                values.Add(string.Empty);
            }

            values[index] = ReadCell(cell, sharedStrings);
        }

        return values;
    }

    private static string ReadCell(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;
        if (type == "s")
        {
            var indexText = cell.Element(Spreadsheet + "v")?.Value ?? "0";
            return int.TryParse(indexText, out var index) && index >= 0 && index < sharedStrings.Count
                ? sharedStrings[index]
                : string.Empty;
        }

        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(Spreadsheet + "t").Select(text => text.Value));
        }

        return cell.Element(Spreadsheet + "v")?.Value ?? string.Empty;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content.Trim());
    }

    private static string GetValue(IReadOnlyList<string> values, int index) =>
        index < values.Count ? values[index].Trim() : string.Empty;

    private static bool IsTrue(string value) =>
        value.Equals("да", StringComparison.OrdinalIgnoreCase)
        || value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("1", StringComparison.OrdinalIgnoreCase);

    private static string ColumnName(int index)
    {
        var dividend = index + 1;
        var name = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            name = Convert.ToChar('A' + modulo) + name;
            dividend = (dividend - modulo) / 26;
        }
        return name;
    }

    private static int ColumnIndex(string cellReference)
    {
        var letters = new string(cellReference.TakeWhile(char.IsLetter).ToArray());
        return letters.Aggregate(0, (sum, letter) => sum * 26 + letter - 'A' + 1) - 1;
    }
}
