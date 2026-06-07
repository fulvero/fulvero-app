import { useEffect, useRef, useState } from 'react'
import type { ChangeEvent, Dispatch, FormEvent, SetStateAction } from 'react'
import * as signalR from '@microsoft/signalr'
import fulveroLogo from './assets/fulvero-logo.png'
import './App.css'

type User = {
  id: string
  companyId: string
  companyName: string
  userName: string
  email: string
  displayName: string
  position: string
  role: string
  avatarUrl: string
  allowedFeatures: string[]
  subscriptionStatus: string
  trialEndsAt: string
  subscriptionPaidUntil?: string
  hasActiveSubscription: boolean
  isSystemCompany: boolean
  isImpersonating: boolean
  homeCompanyId?: string
  homeCompanyName: string
  impersonatedCompanyId?: string
  impersonatedCompanyName: string
  isOnline?: boolean
  lastSeenAt?: string
  unreadCount?: number
}

type ChatMessage = {
  id: string
  senderId: string
  receiverId: string
  text: string
  attachmentFileName: string
  attachmentContentType: string
  hasAttachment: boolean
  createdAt: string
  isOwn: boolean
}

type AuditLog = {
  id: string
  userName: string
  displayName: string
  action: string
  entityType: string
  entityId: string
  details: string
  createdAt: string
}

type SystemHealth = {
  databaseOk: boolean
  serverTime: string
  uptime: string
  machineName: string
  dotnetVersion: string
}

type OzonIntegrationStatus = {
  configured: boolean
  success: boolean
  message: string
  baseUrl: string
  clientIdMasked: string
  apiKeyMasked: string
  checkedAt: string
}

type TelegramIntegrationStatus = {
  botConfigured: boolean
  linked: boolean
  linkCode: string
  startUrl: string
  chatTitle: string
  linkedAt?: string
}

type PlatformDashboard = {
  totalCompanies: number
  trialCompanies: number
  activeSubscriptions: number
  pastDueSubscriptions: number
  totalUsers: number
  ozonConnections: number
  telegramConnections: number
}

type PlatformCompany = {
  id: string
  name: string
  createdAt: string
  subscriptionStatus: string
  subscriptionPaidUntil?: string
  userCount: number
  ozonConnected: boolean
  telegramConnected: boolean
  isSystemCompany: boolean
}

type PlatformUser = {
  id: string
  companyName: string
  userName: string
  email: string
  role: string
  createdAt: string
}

type OzonProduct = {
  productId: number
  offerId: string
  sku?: number
  name: string
  price: number
  oldPrice: number
  minPrice: number
  currencyCode: string
  status: string
  productUrl: string
  imageUrl: string
  productType: ProductType
}

type ProductType = '' | 'Production' | 'Purchase'

type OzonStock = {
  productId: number
  offerId: string
  sku?: number
  name: string
  price: number
  oldPrice: number
  minPrice: number
  currencyCode: string
  fboPresent: number
  fbsPresent: number
  productUrl: string
  imageUrl: string
}

type OzonAnalytics = {
  rows: Array<{
    sku: number
    offerId: string
    productName: string
    status: string
    postingNumber: string
    quantity: number
    revenue: number
    commissionPercent: number
    commissionAmount: number
    payout: number
    currencyCode: string
    logisticsAmount: number
  }>
  topProducts: Array<{
    sku: number
    offerId: string
    productName: string
    quantity: number
    revenue: number
    currencyCode: string
  }>
  dailySales: Array<{
    date: string
    quantity: number
    revenue: number
  }>
  orderedUnitsTotal: number
  revenueTotal: number
  commissionTotal: number
  payoutTotal: number
  logisticsTotal: number
  servicesTotal: number
  awaitingPackagingCount: number
  awaitingDeliverCount: number
  deliveringCount: number
  deliveredCount: number
  cancelledUnitsTotal: number
  accountBalance: {
    amount: number
    currencyCode: string
  }
  timestamp: string
}

type ProductSupplierLink = {
  id?: string
  ozonProductId: number
  offerId: string
  supplierName: string
  supplierUrl: string
}

type SupplierLinkDraft = {
  tempId: string
  id?: string
  supplierName: string
  supplierUrl: string
  isEditing?: boolean
}

type ProductionFile = {
  id: string
  ozonProductId?: number
  offerId: string
  productName: string
  notes: string
  fileName: string
  contentType: string
  createdAt: string
}

type ProductionTask = {
  id: string
  ozonProductId: number
  offerId: string
  productName: string
  requiredQuantity: number
  actualQuantity?: number
  status: 'New' | 'InProgress' | 'Deferred' | 'Completed'
  assignedUserName?: string
  createdAt: string
  startedAt?: string
  deferredAt?: string
  completedAt?: string
  isArchived: boolean
  archivedAt?: string
  items: ProductionTaskItem[]
}

type ProductionTaskItem = {
  id: string
  ozonProductId: number
  offerId: string
  productName: string
  requiredQuantity: number
  actualQuantity?: number
}

type SupplyStatus = 'Created' | 'Sent' | 'Accepted'

type SupplyItem = {
  id: string
  ozonProductId?: number
  offerId: string
  productName: string
  quantity: number
  actualOrderQuantity: number
  supplierName: string
  supplierUrl: string
  isReserve: boolean
}

type SupplyHistoryItem = {
  id: string
  userName: string
  displayName: string
  action: string
  details: string
  createdAt: string
}

type Supply = {
  id: string
  status: SupplyStatus
  createdAt: string
  sentAt?: string
  acceptedAt?: string
  isArchived: boolean
  archivedAt?: string
  items: SupplyItem[]
  history: SupplyHistoryItem[]
}

type SupplyAnalyticsItem = SupplyItem & {
  supplyId: string
  status: SupplyStatus
  createdAt: string
  sentAt?: string
  acceptedAt?: string
}

type DraftSupplyItem = {
  tempId: string
  id?: string
  ozonProductId?: number
  offerId: string
  productName: string
  quantity: number
  actualOrderQuantity: number
  supplierName: string
  supplierUrl: string
  isReserve: boolean
}

type DraftTaskItem = {
  tempId: string
  ozonProductId: number
  offerId: string
  productName: string
  imageUrl: string
  requiredQuantity: number
}

function createTempId() {
  if (globalThis.crypto?.randomUUID) {
    return globalThis.crypto.randomUUID()
  }

  return `${Date.now()}-${Math.random().toString(36).slice(2)}`
}

const tabs = [
  { id: 'dashboard', label: 'Главная' },
  { id: 'production', label: 'Производство' },
  { id: 'products', label: 'Товары' },
  { id: 'analytics', label: 'Аналитика' },
  { id: 'pooling', label: 'Склад' },
  { id: 'supplies', label: 'Поставки' },
  { id: 'integration', label: 'Интеграция', adminOnly: true },
  { id: 'chats', label: 'Чаты' },
  { id: 'users', label: 'Пользователи', adminOnly: true },
  { id: 'settings', label: 'Настройки', adminOnly: true },
  { id: 'platform', label: 'Администрирование', superAdminOnly: true },
] as const

const featureGroups = [
  {
    title: 'Производство',
    items: [
      { id: 'production', label: 'Раздел' },
      { id: 'production.products', label: 'Список товаров' },
      { id: 'production.tasks', label: 'Задачи' },
      { id: 'production.inProgress', label: 'В работе' },
      { id: 'production.deferred', label: 'Отложенные' },
      { id: 'production.completed', label: 'Выполненные' },
      { id: 'production.archive', label: 'Архив задач' },
      { id: 'production.createTask', label: 'Создание задач' },
    ],
  },
  {
    title: 'Поставки',
    items: [
      { id: 'supplies', label: 'Раздел' },
      { id: 'supplies.create', label: 'Создать поставку' },
      { id: 'supplies.editor', label: 'Редактор поставок' },
      { id: 'supplies.all', label: 'Все поставки' },
      { id: 'supplies.archive', label: 'Архив поставок' },
      { id: 'supplies.analytics', label: 'Аналитика по товару' },
    ],
  },
  {
    title: 'Остальное',
    items: [
      { id: 'products', label: 'Товары' },
      { id: 'analytics', label: 'Аналитика' },
      { id: 'analytics.summary', label: 'Сводка аналитики' },
      { id: 'analytics.topProducts', label: 'Топ товары' },
      { id: 'pooling', label: 'Склад' },
      { id: 'pooling.editPrices', label: 'Редактирование цен' },
      { id: 'chats', label: 'Чаты' },
    ],
  },
]
const defaultUserFeatures = ['production', 'production.products', 'production.tasks', 'production.inProgress', 'production.deferred', 'production.completed', 'products', 'supplies', 'supplies.create', 'supplies.all', 'chats']

type TabId = (typeof tabs)[number]['id']
type ProductionSubTab = 'products' | 'tasks' | 'inProgress' | 'deferred' | 'completed' | 'archive'
type SupplySubTab = 'create' | 'editor' | 'all' | 'archive' | 'analytics'
type ProductsSubTab = 'settings' | 'production' | 'purchase' | 'editor'
type AnalyticsSubTab = 'summary' | 'topProducts'
type PlatformSubTab = 'dashboard' | 'companies' | 'users'

function App() {
  const [token, setToken] = useState(() => localStorage.getItem('authToken') ?? '')
  const [user, setUser] = useState<User | null>(() => {
    const value = localStorage.getItem('authUser')
    return value ? JSON.parse(value) : null
  })
  const [users, setUsers] = useState<User[]>([])
  const [auditLogs, setAuditLogs] = useState<AuditLog[]>([])
  const [auditSearch, setAuditSearch] = useState('')
  const [auditStatus, setAuditStatus] = useState('')
  const [systemHealth, setSystemHealth] = useState<SystemHealth | null>(null)
  const [ozonIntegration, setOzonIntegration] = useState<OzonIntegrationStatus | null>(null)
  const [ozonIntegrationStatus, setOzonIntegrationStatus] = useState('')
  const [telegramIntegration, setTelegramIntegration] = useState<TelegramIntegrationStatus | null>(null)
  const [telegramIntegrationStatus, setTelegramIntegrationStatus] = useState('')
  const [ozonClientId, setOzonClientId] = useState('')
  const [ozonApiKey, setOzonApiKey] = useState('')
  const [activeTab, setActiveTab] = useState<TabId>('dashboard')
  const [isLoading, setIsLoading] = useState(true)
  const [passwordResetToken, setPasswordResetToken] = useState(() => new URLSearchParams(window.location.search).get('resetToken') ?? '')
  const [authMode, setAuthMode] = useState<'login' | 'register' | 'forgot' | 'reset'>(() => {
    if (new URLSearchParams(window.location.search).has('resetToken')) {
      return 'reset'
    }

    return window.location.pathname.toLowerCase().includes('register') ? 'register' : 'login'
  })
  const [loginError, setLoginError] = useState('')
  const [passwordResetStatus, setPasswordResetStatus] = useState('')
  const [billingStatus, setBillingStatus] = useState('')
  const [ozonStatus, setOzonStatus] = useState('')
  const [ozonProducts, setOzonProducts] = useState<OzonProduct[]>([])
  const [stockStatus, setStockStatus] = useState('')
  const [ozonStocks, setOzonStocks] = useState<OzonStock[]>([])
  const [stockSearch, setStockSearch] = useState('')
  const [stockSortKey, setStockSortKey] = useState<'fbo' | 'fbs'>('fbo')
  const [urgentStockOnly, setUrgentStockOnly] = useState(false)
  const [urgentStockStatus, setUrgentStockStatus] = useState('')
  const [showUrgentStockModal, setShowUrgentStockModal] = useState(false)
  const [urgentStockResults, setUrgentStockResults] = useState<UrgentStockItem[]>([])
  const [priceStatus, setPriceStatus] = useState('')
  const [editingPrices, setEditingPrices] = useState<Record<number, string>>({})
  const [analyticsStatus, setAnalyticsStatus] = useState('')
  const [analytics, setAnalytics] = useState<OzonAnalytics | null>(null)
  const [productSupplierLinks, setProductSupplierLinks] = useState<Record<number, ProductSupplierLink[]>>({})
  const [supplierModalProduct, setSupplierModalProduct] = useState<OzonProduct | null>(null)
  const [supplierDrafts, setSupplierDrafts] = useState<SupplierLinkDraft[]>([])
  const [productTypeModalProduct, setProductTypeModalProduct] = useState<OzonProduct | null>(null)
  const [productTypeDraft, setProductTypeDraft] = useState<ProductType>('Production')
  const [productsSubTab, setProductsSubTab] = useState<ProductsSubTab>('settings')
  const [analyticsSubTab, setAnalyticsSubTab] = useState<AnalyticsSubTab>('summary')
  const [platformSubTab, setPlatformSubTab] = useState<PlatformSubTab>('dashboard')
  const [platformDashboard, setPlatformDashboard] = useState<PlatformDashboard | null>(null)
  const [platformCompanies, setPlatformCompanies] = useState<PlatformCompany[]>([])
  const [platformUsers, setPlatformUsers] = useState<PlatformUser[]>([])
  const [platformStatus, setPlatformStatus] = useState('')
  const [productEditorSearch, setProductEditorSearch] = useState('')
  const [productionSearch, setProductionSearch] = useState('')
  const [productionSubTab, setProductionSubTab] = useState<ProductionSubTab>('products')
  const [selectedProductionProductId, setSelectedProductionProductId] = useState<number | null>(null)
  const [productionFiles, setProductionFiles] = useState<ProductionFile[]>([])
  const [productionTasks, setProductionTasks] = useState<ProductionTask[]>([])
  const [taskSearch, setTaskSearch] = useState('')
  const [productionStatus, setProductionStatus] = useState('')
  const [taskStatus, setTaskStatus] = useState('')
  const [uploadFile, setUploadFile] = useState<File | null>(null)
  const [selectedTaskProductId, setSelectedTaskProductId] = useState('')
  const [taskQuantity, setTaskQuantity] = useState('')
  const [showCreateTaskModal, setShowCreateTaskModal] = useState(false)
  const [draftTaskItems, setDraftTaskItems] = useState<DraftTaskItem[]>([])
  const [actualQuantities, setActualQuantities] = useState<Record<string, string>>({})
  const [supplySubTab, setSupplySubTab] = useState<SupplySubTab>('create')
  const [supplies, setSupplies] = useState<Supply[]>([])
  const [supplySearch, setSupplySearch] = useState('')
  const [supplyStatusFilter, setSupplyStatusFilter] = useState<'all' | SupplyStatus>('all')
  const [supplyAnalytics, setSupplyAnalytics] = useState<SupplyAnalyticsItem[]>([])
  const [supplyStatus, setSupplyStatus] = useState('')
  const [supplyProductId, setSupplyProductId] = useState('')
  const [supplyQuantity, setSupplyQuantity] = useState('')
  const [reserveProductName, setReserveProductName] = useState('')
  const [reserveQuantity, setReserveQuantity] = useState('')
  const [reserveSupplierName, setReserveSupplierName] = useState('')
  const [reserveSupplierUrl, setReserveSupplierUrl] = useState('')
  const [draftSupplyItems, setDraftSupplyItems] = useState<DraftSupplyItem[]>([])
  const [replaceProducts, setReplaceProducts] = useState<Record<string, string>>({})
  const [supplyActualOrderEdits, setSupplyActualOrderEdits] = useState<Record<string, string>>({})
  const [editingSupplyId, setEditingSupplyId] = useState<string | null>(null)
  const [editSupplyItems, setEditSupplyItems] = useState<DraftSupplyItem[]>([])
  const [editSupplyProductId, setEditSupplyProductId] = useState('')
  const [editSupplyQuantity, setEditSupplyQuantity] = useState('')
  const [editReserveProductName, setEditReserveProductName] = useState('')
  const [editReserveQuantity, setEditReserveQuantity] = useState('')
  const [editReserveSupplierName, setEditReserveSupplierName] = useState('')
  const [editReserveSupplierUrl, setEditReserveSupplierUrl] = useState('')
  const [analyticsProductKey, setAnalyticsProductKey] = useState('')
  const [showCreateSupplyModal, setShowCreateSupplyModal] = useState(false)
  const [supplyImportFile, setSupplyImportFile] = useState<File | null>(null)
  const [newUser, setNewUser] = useState({
    userName: '',
    displayName: '',
    position: '',
    password: '',
    role: 'User',
    allowedFeatures: defaultUserFeatures,
  })
  const [passwordEdits, setPasswordEdits] = useState<Record<string, string>>({})
  const [userSettingsEdits, setUserSettingsEdits] = useState<Record<string, User>>({})
  const [showProfileModal, setShowProfileModal] = useState(false)
  const [profileForm, setProfileForm] = useState({ displayName: '', position: '' })
  const [profileAvatar, setProfileAvatar] = useState<File | null>(null)
  const [profileStatus, setProfileStatus] = useState('')
  const [chatUsers, setChatUsers] = useState<User[]>([])
  const [userSearch, setUserSearch] = useState('')
  const [selectedChatUserId, setSelectedChatUserId] = useState('')
  const [chatMessages, setChatMessages] = useState<ChatMessage[]>([])
  const [chatText, setChatText] = useState('')
  const [chatFile, setChatFile] = useState<File | null>(null)
  const [chatStatus, setChatStatus] = useState('')
  const [showNotifications, setShowNotifications] = useState(false)
  const [seenNewTaskNotificationIds, setSeenNewTaskNotificationIds] = useState<string[]>([])
  const [seenInProgressTaskNotificationIds, setSeenInProgressTaskNotificationIds] = useState<string[]>([])
  const knownNewTaskIdsRef = useRef<Set<string> | null>(null)
  const knownChatUnreadCountsRef = useRef<Record<string, number> | null>(null)
  const knownChatMessageIdsRef = useRef<Record<string, Set<string>>>({})
  const chatMessagesEndRef = useRef<HTMLDivElement | null>(null)
  const selectedChatUserIdRef = useRef('')
  const normalizedProductionSearch = productionSearch.trim().toLowerCase()
  const unsetProducts = ozonProducts.filter((item) => !item.productType)
  const productionProducts = ozonProducts.filter((item) => item.productType === 'Production')
  const purchaseProducts = ozonProducts.filter((item) => item.productType === 'Purchase')
  const normalizedProductEditorSearch = productEditorSearch.trim().toLowerCase()
  const editorProducts = normalizedProductEditorSearch
    ? ozonProducts.filter((item) =>
        [
          item.productId,
          item.offerId,
          item.sku,
          item.name,
          item.price,
          item.oldPrice,
          item.minPrice,
          item.status,
          item.productUrl,
          formatProductType(item.productType),
        ]
          .filter((value) => value !== undefined && value !== null)
          .some((value) => String(value).toLowerCase().includes(normalizedProductEditorSearch)),
      )
    : ozonProducts
  const filteredProductionProducts = normalizedProductionSearch
    ? productionProducts.filter((item) =>
        [
          item.productId,
          item.offerId,
          item.sku,
          item.name,
          item.price,
          item.oldPrice,
          item.minPrice,
          item.currencyCode,
          item.status,
          item.productUrl,
        ]
          .filter((value) => value !== undefined && value !== null)
          .some((value) => String(value).toLowerCase().includes(normalizedProductionSearch)),
      )
    : productionProducts
  const productsBySubTab =
    productsSubTab === 'settings'
      ? unsetProducts
      : productsSubTab === 'production'
        ? productionProducts
        : productsSubTab === 'purchase'
          ? purchaseProducts
          : editorProducts
  const normalizedTaskSearch = taskSearch.trim().toLowerCase()
  const filteredProductionTasks = normalizedTaskSearch
    ? productionTasks.filter((task) => matchesProductionTask(task, normalizedTaskSearch))
    : productionTasks
  const allNewProductionTasks = productionTasks.filter((task) => task.status === 'New' && !task.isArchived)
  const allInProgressProductionTasks = productionTasks.filter((task) => task.status === 'InProgress' && !task.isArchived)
  const newProductionTasks = filteredProductionTasks.filter((task) => task.status === 'New' && !task.isArchived)
  const inProgressProductionTasks = filteredProductionTasks.filter((task) => task.status === 'InProgress' && !task.isArchived)
  const deferredProductionTasks = filteredProductionTasks.filter((task) => task.status === 'Deferred' && !task.isArchived)
  const completedProductionTasks = filteredProductionTasks.filter((task) => task.status === 'Completed' && !task.isArchived)
  const archivedProductionTasks = filteredProductionTasks.filter((task) => task.isArchived)
  const selectedProductionProduct = ozonProducts.find(
    (item) => item.productId === selectedProductionProductId,
  )
  const filteredSupplyAnalytics = analyticsProductKey
    ? supplyAnalytics.filter((item) =>
        item.isReserve
          ? `reserve:${item.productName}` === analyticsProductKey
          : `product:${item.ozonProductId}` === analyticsProductKey,
      )
    : supplyAnalytics
  const normalizedSupplySearch = supplySearch.trim().toLowerCase()
  const searchedSupplies = normalizedSupplySearch
    ? supplies.filter((supply) => matchesSupply(supply, normalizedSupplySearch))
    : supplies
  const activeSupplies = searchedSupplies.filter((supply) => !supply.isArchived)
  const archivedSupplies = searchedSupplies.filter((supply) => supply.isArchived)
  const createdSupplies = activeSupplies.filter((supply) => supply.status === 'Created')
  const editableSupplies = activeSupplies.filter((supply) => supply.status !== 'Created')
  const visibleAllSupplies = activeSupplies.filter((supply) =>
    supplyStatusFilter === 'all' ? true : supply.status === supplyStatusFilter,
  )
  const normalizedStockSearch = stockSearch.trim().toLowerCase()
  const filteredOzonStocks = normalizedStockSearch
    ? ozonStocks.filter((stock) =>
        [stock.name, stock.offerId, stock.sku, stock.productId, stock.price, stock.currencyCode]
          .filter((value) => value !== undefined && value !== null)
          .some((value) => String(value).toLowerCase().includes(normalizedStockSearch)),
      )
    : ozonStocks
  const normalizedUserSearch = userSearch.trim().toLowerCase()
  const filteredUsers = normalizedUserSearch
    ? users.filter((item) =>
        [item.id, item.userName, item.displayName, item.position, item.role]
          .filter((value) => value !== undefined && value !== null)
          .some((value) => String(value).toLowerCase().includes(normalizedUserSearch)),
      )
    : users
  const topAnalyticsProducts = (analytics?.topProducts ?? [])
    .map((row) => ({
      ...row,
      key: row.sku ? `sku:${row.sku}` : `offer:${row.offerId}`,
    }))
    .sort((left, right) => right.quantity - left.quantity)
  const dashboardSalesDays = buildDashboardSalesDays(analytics)
  const maxDashboardSales = Math.max(...dashboardSalesDays.map((item) => item.quantity), 1)
  const urgentStockItems = getUrgentStockItems(ozonStocks, analytics)
  const urgentProductionItems = urgentStockItems.filter((item) =>
    productionProducts.some((product) => product.productId === item.productId),
  )
  const urgentPurchaseItems = urgentStockItems.filter((item) =>
    purchaseProducts.some((product) => product.productId === item.productId),
  )
  const visibleOzonStocks = urgentStockOnly
    ? filteredOzonStocks.filter((stock) =>
        urgentStockItems.some((urgentItem) => urgentItem.productId === stock.productId),
      )
    : filteredOzonStocks
  const sortedOzonStocks = [...visibleOzonStocks].sort((left, right) => {
    const leftValue = getStockSortValue(left, stockSortKey)
    const rightValue = getStockSortValue(right, stockSortKey)
    return leftValue - rightValue
  })
  const selectedChatUser = chatUsers.find((item) => item.id === selectedChatUserId)
  const chatUnreadTotal = chatUsers.reduce((sum, item) => sum + (item.unreadCount ?? 0), 0)
  const unseenNewProductionTasks = allNewProductionTasks.filter(
    (task) => !seenNewTaskNotificationIds.includes(task.id),
  )
  const unseenInProgressProductionTasks = allInProgressProductionTasks.filter(
    (task) => !seenInProgressTaskNotificationIds.includes(task.id),
  )
  const notificationItems = [
    ...(unseenNewProductionTasks.length > 0
      ? [{ key: 'tasks-new', label: `Новые задачи: ${unseenNewProductionTasks.length}`, target: 'tasks' as const }]
      : []),
    ...(unseenInProgressProductionTasks.length > 0
      ? [{ key: 'tasks-work', label: `В работе: ${unseenInProgressProductionTasks.length}`, target: 'inProgress' as const }]
      : []),
    ...chatUsers
      .filter((item) => (item.unreadCount ?? 0) > 0)
      .map((item) => ({
        key: `chat-${item.id}`,
        label: `${item.displayName || item.userName}: ${item.unreadCount} новых сообщений`,
        target: 'chat' as const,
        userId: item.id,
      })),
  ]
  const productionNotificationTotal = unseenNewProductionTasks.length
  const notificationTotal = unseenNewProductionTasks.length + unseenInProgressProductionTasks.length + chatUnreadTotal
  const isSuperAdmin = user?.role === 'SuperAdmin'
  const isAdminLike = user?.role === 'Admin' || isSuperAdmin
  const hasFeature = (feature: string) =>
    isAdminLike || Boolean(user?.allowedFeatures?.includes(feature))
  const hasSubFeature = (feature: string, _fallback: string) => hasFeature(feature)
  const visibleTabs = tabs.filter((tab) => {
    if ('superAdminOnly' in tab) {
      return isSuperAdmin
    }

    if (tab.id === 'dashboard') {
      return true
    }

    if ('adminOnly' in tab) {
      return isAdminLike
    }

    return hasFeature(tab.id)
  })

  useEffect(() => {
    if (!token) {
      setIsLoading(false)
      return
    }

    loadCurrentUser()
    setIsLoading(false)
  }, [token])

  useEffect(() => {
    if (!user?.id) {
      setSeenNewTaskNotificationIds([])
      setSeenInProgressTaskNotificationIds([])
      return
    }

    setSeenNewTaskNotificationIds(readStringListFromStorage(getTaskNotificationStorageKey(user.id, 'new')))
    setSeenInProgressTaskNotificationIds(readStringListFromStorage(getTaskNotificationStorageKey(user.id, 'in-progress')))
  }, [user?.id])

  useEffect(() => {
    if (!user || visibleTabs.some((tab) => tab.id === activeTab)) {
      return
    }

    setActiveTab(visibleTabs[0]?.id ?? 'dashboard')
  }, [activeTab, user, visibleTabs])

  useEffect(() => {
    if (isAdminLike) {
      return
    }

    const productionFallbacks: Array<[ProductionSubTab, string]> = [
      ['products', 'production.products'],
      ['tasks', 'production.tasks'],
      ['inProgress', 'production.inProgress'],
      ['deferred', 'production.deferred'],
      ['completed', 'production.completed'],
      ['archive', 'production.archive'],
    ]
    if (activeTab === 'production' && !hasSubFeature(`production.${productionSubTab}`, 'production')) {
      setProductionSubTab(productionFallbacks.find(([, feature]) => hasSubFeature(feature, 'production'))?.[0] ?? 'products')
    }

    const supplyFallbacks: Array<[SupplySubTab, string]> = [
      ['create', 'supplies.create'],
      ['editor', 'supplies.editor'],
      ['all', 'supplies.all'],
      ['archive', 'supplies.archive'],
      ['analytics', 'supplies.analytics'],
    ]
    if (activeTab === 'supplies' && !hasSubFeature(`supplies.${supplySubTab}`, 'supplies')) {
      setSupplySubTab(supplyFallbacks.find(([, feature]) => hasSubFeature(feature, 'supplies'))?.[0] ?? 'create')
    }

    const analyticsFallbacks: Array<[AnalyticsSubTab, string]> = [
      ['summary', 'analytics.summary'],
      ['topProducts', 'analytics.topProducts'],
    ]
    if (activeTab === 'analytics' && !hasSubFeature(`analytics.${analyticsSubTab}`, 'analytics')) {
      setAnalyticsSubTab(analyticsFallbacks.find(([, feature]) => hasSubFeature(feature, 'analytics'))?.[0] ?? 'summary')
    }
  }, [activeTab, user, isAdminLike, productionSubTab, supplySubTab, analyticsSubTab])

  useEffect(() => {
    if (!token || !isAdminLike) {
      return
    }

    loadUsers()
    loadAuditLogs()
    loadSystemHealth()
    loadOzonIntegrationStatus()
    loadTelegramIntegrationStatus()
    const intervalId = window.setInterval(() => {
      loadUsers()
      loadAuditLogs()
      loadSystemHealth()
    }, 30000)
    return () => window.clearInterval(intervalId)
  }, [token, isAdminLike])

  useEffect(() => {
    setProfileForm({
      displayName: user?.displayName ?? '',
      position: user?.position ?? '',
    })
  }, [user?.displayName, user?.position])

  useEffect(() => {
    if (!token) {
      return
    }

    requestBrowserNotifications()
    sendHeartbeat()
    const intervalId = window.setInterval(sendHeartbeat, 30000)
    return () => window.clearInterval(intervalId)
  }, [token])

  useEffect(() => {
    selectedChatUserIdRef.current = selectedChatUserId
  }, [selectedChatUserId])

  useEffect(() => {
    if (activeTab === 'chats' && selectedChatUserId) {
      markChatNotificationsSeen(selectedChatUserId)
    }
  }, [activeTab, selectedChatUserId])

  useEffect(() => {
    if (activeTab === 'chats') {
      chatMessagesEndRef.current?.scrollIntoView({ block: 'end' })
    }
  }, [activeTab, selectedChatUserId, chatMessages.length])

  useEffect(() => {
    if (!token) {
      return
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/live', {
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect()
      .build()

    connection.on('ProductionTasksChanged', () => {
      loadProductionTasks()
    })

    connection.on('ChatMessagesChanged', (senderId: string, receiverId: string) => {
      if (user?.id !== senderId && user?.id !== receiverId) {
        return
      }

      loadChatUsers()
      const activeChatUserId = selectedChatUserIdRef.current
      if (activeChatUserId && (activeChatUserId === senderId || activeChatUserId === receiverId)) {
        loadChatMessages(activeChatUserId)
      }
    })

    connection.start().catch(() => {
      setTaskStatus('Live-уведомления временно недоступны')
    })

    return () => {
      connection.stop()
    }
  }, [token, user?.id])

  useEffect(() => {
    if (!token) {
      return
    }

    loadProductionFiles('')
    loadProductionTasks()
    loadSupplies()
    loadSupplyAnalytics()
  }, [token])

  useEffect(() => {
    if (!token) {
      return
    }

    loadOzonProducts()
    loadProductSupplierLinks()
    if (hasFeature('analytics')) {
      loadAnalytics()
    }
  }, [token, user?.role, user?.allowedFeatures])

  useEffect(() => {
    if (!token || activeTab !== 'pooling' || !hasFeature('pooling')) {
      return
    }

    loadOzonStocks()
  }, [token, activeTab])

  useEffect(() => {
    if (!token || activeTab !== 'platform' || !isSuperAdmin) {
      return
    }

    loadPlatformData()
  }, [token, activeTab, platformSubTab, isSuperAdmin])

  useEffect(() => {
    if (!token) {
      return
    }

    loadChatUsers()
    const intervalId = window.setInterval(loadChatUsers, 30000)
    return () => window.clearInterval(intervalId)
  }, [token])

  useEffect(() => {
    if (!token || !selectedChatUserId) {
      return
    }

    loadChatMessages(selectedChatUserId)
    const intervalId = window.setInterval(() => loadChatMessages(selectedChatUserId), 5000)
    return () => window.clearInterval(intervalId)
  }, [token, selectedChatUserId])

  async function handleLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLoginError('')

    const formData = new FormData(event.currentTarget)
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        companyName: formData.get('companyName'),
        userName: formData.get('userName'),
        password: formData.get('password'),
      }),
    })

    if (!response.ok) {
      setLoginError('Неверная компания, логин/email или пароль')
      return
    }

    const data = await response.json()
    localStorage.setItem('authToken', data.token)
    localStorage.setItem('authUser', JSON.stringify(data.user))
    setToken(data.token)
    setUser(data.user)
  }

  async function handleRegister(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLoginError('')

    const formData = new FormData(event.currentTarget)
    const response = await fetch('/api/auth/register', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        companyName: formData.get('companyName'),
        userName: formData.get('userName'),
        email: formData.get('email'),
        displayName: formData.get('displayName'),
        password: formData.get('password'),
      }),
    })

    if (!response.ok) {
      setLoginError(getApiErrorMessage(await response.text(), 'Не удалось зарегистрировать компанию'))
      return
    }

    const data = await response.json()
    localStorage.setItem('authToken', data.token)
    localStorage.setItem('authUser', JSON.stringify(data.user))
    setToken(data.token)
    setUser(data.user)
  }

  async function handlePasswordResetRequest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLoginError('')
    setPasswordResetStatus('')

    const formData = new FormData(event.currentTarget)
    const response = await fetch('/api/auth/password-reset/request', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        companyName: formData.get('companyName'),
        userName: formData.get('userName'),
      }),
    })

    if (!response.ok) {
      setLoginError(getApiErrorMessage(await response.text(), 'Не удалось отправить письмо для сброса пароля'))
      return
    }

    const data: { message: string } = await response.json()
    setPasswordResetStatus(data.message)
  }

  async function handlePasswordResetConfirm(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLoginError('')
    setPasswordResetStatus('')

    const formData = new FormData(event.currentTarget)
    const password = String(formData.get('password') ?? '')
    const passwordRepeat = String(formData.get('passwordRepeat') ?? '')
    if (password !== passwordRepeat) {
      setLoginError('Пароли не совпадают')
      return
    }

    const response = await fetch('/api/auth/password-reset/confirm', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        token: passwordResetToken,
        password,
      }),
    })

    if (!response.ok) {
      setLoginError(getApiErrorMessage(await response.text(), 'Не удалось обновить пароль'))
      return
    }

    const data: { message: string } = await response.json()
    setPasswordResetStatus(data.message)
    setPasswordResetToken('')
    window.history.replaceState(null, '', window.location.pathname)
    setAuthMode('login')
  }

  async function startSubscriptionCheckout() {
    setBillingStatus('Готовим оплату подписки через ЮKassa...')
    const response = await fetch('/api/billing/create-payment', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setBillingStatus(getApiErrorMessage(await response.text(), 'Не удалось создать платеж ЮKassa'))
      return
    }

    const data: { confirmationUrl: string } = await response.json()
    if (!data.confirmationUrl) {
      setBillingStatus('ЮKassa не вернула ссылку на оплату')
      return
    }

    window.location.href = data.confirmationUrl
  }

  function logout() {
    if (token) {
      fetch('/api/auth/logout', {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
        },
      }).catch(() => undefined)
    }

    localStorage.removeItem('authToken')
    localStorage.removeItem('authUser')
    setToken('')
    setUser(null)
    setUsers([])
    setChatUsers([])
    setChatMessages([])
    setSelectedChatUserId('')
    knownNewTaskIdsRef.current = null
    knownChatUnreadCountsRef.current = null
    knownChatMessageIdsRef.current = {}
    selectedChatUserIdRef.current = ''
  }

  function confirmLogout() {
    if (!window.confirm('Выйти из аккаунта?')) {
      return
    }

    logout()
  }

  function requestBrowserNotifications() {
    if ('Notification' in window && Notification.permission === 'default') {
      Notification.requestPermission().catch(() => undefined)
    }
  }

  function showBrowserNotification(title: string, body: string) {
    if (!('Notification' in window) || Notification.permission !== 'granted') {
      return
    }

    new Notification(title, { body })
  }

  async function loadCurrentUser() {
    const response = await fetch('/api/auth/me', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      return
    }

    const data: User = await response.json()
    localStorage.setItem('authUser', JSON.stringify(data))
    setUser(data)
  }

  async function sendHeartbeat() {
    try {
      await fetch('/api/auth/heartbeat', {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
        },
      })
    } catch {
      // Следующий heartbeat повторит отметку активности.
    }
  }

  async function loadUsers() {
    const response = await fetch('/api/admin/users', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      return
    }

    const data: User[] = await response.json()
    setUsers(data)
  }

  async function loadAuditLogs(search = auditSearch) {
    const params = new URLSearchParams()
    if (search.trim()) {
      params.set('search', search.trim())
    }

    const response = await fetch(`/api/admin/audit-logs?${params.toString()}`, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setAuditStatus('Не удалось загрузить журнал действий')
      return
    }

    const data: AuditLog[] = await response.json()
    setAuditLogs(data)
    setAuditStatus(`Записей: ${data.length}`)
  }

  async function exportAuditLogs() {
    const response = await fetch('/api/admin/audit-logs/export', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setAuditStatus('Не удалось скачать журнал')
      return
    }

    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `audit-log-${new Date().toISOString().slice(0, 10)}.csv`
    link.click()
    URL.revokeObjectURL(url)
  }

  async function loadSystemHealth() {
    const response = await fetch('/api/admin/system-health', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      return
    }

    const data: SystemHealth = await response.json()
    setSystemHealth(data)
  }

  async function loadPlatformData() {
    setPlatformStatus('Загружаем данные платформы...')
    const headers = {
      Authorization: `Bearer ${token}`,
    }

    const [dashboardResponse, companiesResponse, usersResponse] = await Promise.all([
      fetch('/api/platform/dashboard', { headers }),
      fetch('/api/platform/companies', { headers }),
      fetch('/api/platform/users', { headers }),
    ])

    if (!dashboardResponse.ok || !companiesResponse.ok || !usersResponse.ok) {
      setPlatformStatus('Не удалось загрузить администрирование')
      return
    }

    setPlatformDashboard(await dashboardResponse.json())
    setPlatformCompanies(await companiesResponse.json())
    setPlatformUsers(await usersResponse.json())
    setPlatformStatus('')
  }

  async function runPlatformAction(path: string, options: RequestInit = {}) {
    setPlatformStatus('Выполняем действие...')
    const response = await fetch(path, {
      ...options,
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
        ...(options.headers ?? {}),
      },
    })

    if (!response.ok) {
      setPlatformStatus(getApiErrorMessage(await response.text(), 'Не удалось выполнить действие'))
      return null
    }

    await loadPlatformData()
    setPlatformStatus('Готово')
    return response
  }

  async function extendCompanyTrial(company: PlatformCompany) {
    const value = window.prompt(`На сколько дней продлить триал компании ${company.name}?`, '3')
    if (!value) {
      return
    }

    const days = Number(value)
    await runPlatformAction(`/api/platform/companies/${company.id}/extend-trial`, {
      method: 'POST',
      body: JSON.stringify({ days: Number.isFinite(days) ? days : 3 }),
    })
  }

  async function changeCompanySubscription(company: PlatformCompany) {
    const status = window.prompt('Статус подписки: Trial, Active, PastDue, Blocked', company.subscriptionStatus)
    if (!status) {
      return
    }

    const paidUntil = window.prompt('Дата окончания подписки в формате YYYY-MM-DD или пусто', company.subscriptionPaidUntil?.slice(0, 10) ?? '')
    await runPlatformAction(`/api/platform/companies/${company.id}/subscription`, {
      method: 'PUT',
      body: JSON.stringify({
        status,
        trialEndsAt: status === 'Trial' ? new Date(Date.now() + 3 * 24 * 60 * 60 * 1000).toISOString() : null,
        subscriptionPaidUntil: paidUntil ? new Date(`${paidUntil}T23:59:59`).toISOString() : null,
      }),
    })
  }

  async function blockCompany(company: PlatformCompany) {
    if (!window.confirm(`Заблокировать компанию ${company.name}?`)) {
      return
    }

    await runPlatformAction(`/api/platform/companies/${company.id}/block`, { method: 'POST' })
  }

  async function unblockCompany(company: PlatformCompany) {
    await runPlatformAction(`/api/platform/companies/${company.id}/unblock`, { method: 'POST' })
  }

  async function impersonateCompany(company: PlatformCompany) {
    if (!window.confirm(`Войти как компания ${company.name}?`)) {
      return
    }

    const response = await runPlatformAction(`/api/platform/companies/${company.id}/impersonate`, { method: 'POST' })
    if (!response) {
      return
    }

    const data: { token: string; user: User } = await response.json()
    localStorage.setItem('authToken', data.token)
    localStorage.setItem('authUser', JSON.stringify(data.user))
    setToken(data.token)
    setUser(data.user)
    setActiveTab('dashboard')
  }

  async function returnToFulvero() {
    const response = await fetch('/api/platform/return', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      return
    }

    const data: { token: string; user: User } = await response.json()
    localStorage.setItem('authToken', data.token)
    localStorage.setItem('authUser', JSON.stringify(data.user))
    setToken(data.token)
    setUser(data.user)
    setActiveTab('platform')
  }

  async function loadOzonIntegrationStatus() {
    setOzonIntegrationStatus('Проверяем Ozon API...')
    const response = await fetch('/api/integrations/ozon', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setOzonIntegrationStatus('Не удалось проверить Ozon API')
      return
    }

    const data: OzonIntegrationStatus = await response.json()
    setOzonIntegration(data)
    setOzonIntegrationStatus(data.message)
  }

  async function saveOzonIntegration(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setOzonIntegrationStatus('Сохраняем Ozon ID и ключ...')

    const response = await fetch('/api/integrations/ozon', {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        clientId: ozonClientId,
        apiKey: ozonApiKey,
      }),
    })

    if (!response.ok) {
      setOzonIntegrationStatus(getApiErrorMessage(await response.text(), 'Не удалось сохранить Ozon-интеграцию'))
      return
    }

    const data: OzonIntegrationStatus = await response.json()
    setOzonIntegration(data)
    setOzonIntegrationStatus(data.message)
    setOzonClientId('')
    setOzonApiKey('')
  }

  async function loadTelegramIntegrationStatus() {
    setTelegramIntegrationStatus('Проверяем Telegram...')
    const response = await fetch('/api/integrations/telegram', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setTelegramIntegrationStatus('Не удалось проверить Telegram')
      return
    }

    const data: TelegramIntegrationStatus = await response.json()
    setTelegramIntegration(data)
    setTelegramIntegrationStatus(data.linked ? 'Telegram подключен' : 'Telegram ожидает привязки')
  }

  async function regenerateTelegramLink() {
    setTelegramIntegrationStatus('Создаем новый код привязки...')
    const response = await fetch('/api/integrations/telegram/regenerate', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setTelegramIntegrationStatus(getApiErrorMessage(await response.text(), 'Не удалось обновить код Telegram'))
      return
    }

    const data: TelegramIntegrationStatus = await response.json()
    setTelegramIntegration(data)
    setTelegramIntegrationStatus('Новый код создан. Запустите бота по ссылке.')
  }

  async function sendTelegramTest() {
    setTelegramIntegrationStatus('Отправляем тестовое уведомление...')
    const response = await fetch('/api/integrations/telegram/test', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setTelegramIntegrationStatus(getApiErrorMessage(await response.text(), 'Не удалось отправить тест Telegram'))
      return
    }

    const data: { message: string } = await response.json()
    setTelegramIntegrationStatus(data.message)
  }

  async function unlinkTelegram() {
    if (!window.confirm('Отвязать Telegram-чат от компании?')) {
      return
    }

    const response = await fetch('/api/integrations/telegram', {
      method: 'DELETE',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setTelegramIntegrationStatus('Не удалось отвязать Telegram')
      return
    }

    await loadTelegramIntegrationStatus()
    setTelegramIntegrationStatus('Telegram-чат отвязан')
  }

  async function exportTaskArchive() {
    const response = await fetch('/api/production/tasks/archive/export', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setTaskStatus('Не удалось скачать архив задач')
      return
    }

    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `production-task-archive-${new Date().toISOString().slice(0, 10)}.csv`
    link.click()
    URL.revokeObjectURL(url)
  }

  async function exportSupplyAnalytics() {
    const response = await fetch('/api/supplies/analytics/export', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setSupplyStatus('Не удалось скачать аналитику поставок')
      return
    }

    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `supplies-analytics-${new Date().toISOString().slice(0, 10)}.csv`
    link.click()
    URL.revokeObjectURL(url)
  }

  async function loadChatUsers() {
    const response = await fetch('/api/chat/users', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      return
    }

    const data: User[] = await response.json()
    const previousUnreadCounts = knownChatUnreadCountsRef.current
    const nextUnreadCounts = Object.fromEntries(
      data.map((item) => [item.id, item.unreadCount ?? 0]),
    )

    if (previousUnreadCounts) {
      data.forEach((item) => {
        const previousCount = previousUnreadCounts[item.id] ?? 0
        const currentCount = item.unreadCount ?? 0
        if (currentCount > previousCount) {
          showBrowserNotification(
            'Новое сообщение',
            `${item.displayName || item.userName}: ${currentCount - previousCount} новое`,
          )
        }
      })
    }

    knownChatUnreadCountsRef.current = nextUnreadCounts
    setChatUsers(data)
    setSelectedChatUserId((current) => current || data[0]?.id || '')
  }

  async function loadChatMessages(chatUserId = selectedChatUserId) {
    if (!chatUserId) {
      setChatMessages([])
      return
    }

    const response = await fetch(`/api/chat/${chatUserId}/messages`, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      return
    }

    const data: ChatMessage[] = await response.json()
    const previousMessageIds = knownChatMessageIdsRef.current[chatUserId]
    if (previousMessageIds) {
      const incomingMessages = data.filter((message) => !message.isOwn && !previousMessageIds.has(message.id))
      if (incomingMessages.length > 0) {
        const lastMessage = incomingMessages[incomingMessages.length - 1]
        showBrowserNotification(
          'Новое сообщение',
          lastMessage.text || lastMessage.attachmentFileName || 'Вложение',
        )
      }
    }

    knownChatMessageIdsRef.current[chatUserId] = new Set(data.map((message) => message.id))
    setChatMessages(data)
    await loadChatUsers()
  }

  async function sendChatMessage(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!selectedChatUserId || (!chatText.trim() && !chatFile)) {
      setChatStatus('Выберите пользователя и напишите сообщение или прикрепите файл')
      return
    }

    const formData = new FormData()
    formData.append('text', chatText)
    if (chatFile) {
      formData.append('file', chatFile)
    }

    const response = await fetch(`/api/chat/${selectedChatUserId}/messages`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
      },
      body: formData,
    })

    if (!response.ok) {
      setChatStatus('Не удалось отправить сообщение')
      return
    }

    setChatText('')
    setChatFile(null)
    setChatStatus('')
    await loadChatMessages(selectedChatUserId)
  }

  async function deleteChatMessage(id: string) {
    const response = await fetch(`/api/chat/messages/${id}`, {
      method: 'DELETE',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setChatStatus('Не удалось удалить сообщение')
      return
    }

    setChatStatus('Сообщение удалено')
    await loadChatMessages(selectedChatUserId)
  }

  async function downloadChatAttachment(message: ChatMessage) {
    const response = await fetch(`/api/chat/messages/${message.id}/attachment`, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setChatStatus('Не удалось скачать вложение')
      return
    }

    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')

    link.href = url
    link.download = message.attachmentFileName || 'chat-file'
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
  }

  async function createUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const response = await fetch('/api/admin/users', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(newUser),
    })

    if (!response.ok) {
      return
    }

    const createdUser = await response.json()
    setUsers((current) => [...current, createdUser])
    setNewUser({
      userName: '',
      displayName: '',
      position: '',
      password: '',
      role: 'User',
      allowedFeatures: defaultUserFeatures,
    })
  }

  async function saveUserSettings(id: string) {
    const edit = userSettingsEdits[id]
    if (!edit) {
      return
    }

    const response = await fetch(`/api/admin/users/${id}/settings`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        displayName: edit.displayName,
        position: edit.position,
        role: edit.role,
        allowedFeatures: edit.allowedFeatures,
      }),
    })

    if (!response.ok) {
      return
    }

    const updatedUser: User = await response.json()
    setUsers((current) => current.map((item) => (item.id === id ? updatedUser : item)))
    setUserSettingsEdits((current) => {
      const next = { ...current }
      delete next[id]
      return next
    })
  }

  async function deleteUser(id: string) {
    const response = await fetch(`/api/admin/users/${id}`, {
      method: 'DELETE',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      return
    }

    setUsers((current) => current.filter((item) => item.id !== id))
  }

  async function changeUserPassword(id: string) {
    const password = passwordEdits[id]
    if (!password) {
      return
    }

    const response = await fetch(`/api/admin/users/${id}/password`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ password }),
    })

    if (!response.ok) {
      return
    }

    setPasswordEdits((current) => ({ ...current, [id]: '' }))
  }

  async function saveProfile(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setProfileStatus('Сохраняем профиль...')

    const response = await fetch('/api/profile', {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(profileForm),
    })

    if (!response.ok) {
      setProfileStatus('Не удалось сохранить профиль')
      return
    }

    const updatedUser: User = await response.json()
    localStorage.setItem('authUser', JSON.stringify(updatedUser))
    setUser(updatedUser)
    setProfileStatus('Профиль сохранен')
  }

  async function uploadProfileAvatar() {
    if (!profileAvatar) {
      setProfileStatus('Выберите фотографию')
      return
    }

    const formData = new FormData()
    formData.append('avatar', profileAvatar)
    setProfileStatus('Загружаем фото...')

    const response = await fetch('/api/profile/avatar', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
      },
      body: formData,
    })

    if (!response.ok) {
      setProfileStatus(
        response.status === 413
          ? 'Фотография слишком большая. Выберите фото меньше или сожмите изображение.'
          : await response.text() || 'Не удалось загрузить фото',
      )
      return
    }

    const updatedUser: User = await response.json()
    localStorage.setItem('authUser', JSON.stringify(updatedUser))
    setUser(updatedUser)
    setProfileAvatar(null)
    setProfileStatus('Фото обновлено')
  }

  async function handleProfileAvatarChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0] ?? null
    if (!file) {
      setProfileAvatar(null)
      return
    }

    if (!file.type.startsWith('image/')) {
      setProfileAvatar(null)
      setProfileStatus('Выберите файл изображения')
      return
    }

    setProfileStatus('Подготавливаем фото...')
    try {
      const prepared = await prepareAvatarFile(file)
      setProfileAvatar(prepared)
      setProfileStatus(prepared.size < file.size ? 'Фото сжато и готово к загрузке' : 'Фото готово к загрузке')
    } catch {
      setProfileAvatar(file)
      setProfileStatus('Не удалось сжать фото, попробуйте загрузить исходный файл')
    } finally {
      event.target.value = ''
    }
  }

  async function loadOzonProducts() {
    setOzonStatus('Загружаем товары Ozon...')

    const response = await fetch('/api/ozon/products', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      if (response.status === 401) {
        logout()
        setLoginError('Сессия истекла. Войдите заново.')
        return
      }

      setOzonStatus(
        response.status === 403
          ? 'Нет доступа к списку товаров Ozon'
          : getApiErrorMessage(await response.text(), 'Не удалось получить данные Ozon'),
      )
      return
    }

    const data: OzonProduct[] = await response.json()
    setOzonProducts(data)
    setOzonStatus(`Загружено товаров Ozon: ${data.length}`)
  }

  async function loadProductSupplierLinks() {
    const response = await fetch('/api/product-supplier-links', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      return
    }

    const data: ProductSupplierLink[] = await response.json()
    setProductSupplierLinks(
      data.reduce<Record<number, ProductSupplierLink[]>>((acc, item) => {
        acc[item.ozonProductId] = [...(acc[item.ozonProductId] ?? []), item]
        return acc
      }, {}),
    )
  }

  function openSupplierModal(product: OzonProduct) {
    if (product.productType !== 'Purchase') {
      setOzonStatus('Поставщика можно добавить только к закупочному товару')
      return
    }

    setSupplierModalProduct(product)
    setSupplierDrafts(
      (productSupplierLinks[product.productId] ?? []).map((item) => ({
        tempId: item.id ?? createTempId(),
        id: item.id,
        supplierName: item.supplierName,
        supplierUrl: item.supplierUrl,
        isEditing: false,
      })),
    )
  }

  function addSupplierDraft() {
    setSupplierDrafts((current) => [
      ...current,
      { tempId: createTempId(), supplierName: '', supplierUrl: '', isEditing: true },
    ])
  }

  async function saveSupplierLinks() {
    if (!supplierModalProduct) {
      return
    }

    const response = await fetch(`/api/product-supplier-links/${supplierModalProduct.productId}`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        offerId: supplierModalProduct.offerId,
        suppliers: supplierDrafts.map(({ supplierName, supplierUrl }) => ({
          supplierName,
          supplierUrl,
        })),
      }),
    })

    if (!response.ok) {
      setOzonStatus(getApiErrorMessage(await response.text(), 'Не удалось сохранить поставщиков'))
      return
    }

    const updated: ProductSupplierLink[] = await response.json()
    setProductSupplierLinks((current) => ({
      ...current,
      [supplierModalProduct.productId]: updated,
    }))
    setSupplierModalProduct(null)
    setSupplierDrafts([])
    setOzonStatus('Поставщики товара сохранены')
  }

  async function deleteSupplierLinks(product: OzonProduct) {
    if (!window.confirm(`Удалить всех поставщиков для ${product.offerId}?`)) {
      return
    }

    const response = await fetch(`/api/product-supplier-links/${product.productId}`, {
      method: 'DELETE',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok && response.status !== 404) {
      setOzonStatus(getApiErrorMessage(await response.text(), 'Не удалось удалить ссылку поставщика'))
      return
    }

    setProductSupplierLinks((current) => {
      const next = { ...current }
      delete next[product.productId]
      return next
    })
    setOzonStatus('Поставщики товара удалены')
  }

  function openProductTypeModal(product: OzonProduct) {
    setProductTypeModalProduct(product)
    setProductTypeDraft(product.productType || 'Production')
  }

  async function updateProductType(product: OzonProduct, productType: ProductType) {
    if (!productType) {
      return false
    }

    const response = await fetch(`/api/product-settings/${product.productId}/type`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        offerId: product.offerId,
        productName: product.name,
        productType,
      }),
    })

    if (!response.ok) {
      setOzonStatus(getApiErrorMessage(await response.text(), 'Не удалось сохранить тип товара'))
      return false
    }

    setOzonProducts((current) =>
      current.map((item) => (item.productId === product.productId ? { ...item, productType } : item)),
    )
    setOzonStatus('Тип товара сохранен')
    return true
  }

  async function saveProductTypeFromModal() {
    if (!productTypeModalProduct || !productTypeDraft) {
      return
    }

    const isSaved = await updateProductType(productTypeModalProduct, productTypeDraft)
    if (isSaved) {
      setProductTypeModalProduct(null)
    }
  }

  async function loadOzonStocks() {
    setStockStatus('Загружаем остатки со склада Ozon...')

    const response = await fetch('/api/ozon/stocks', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setStockStatus(getApiErrorMessage(await response.text(), 'Не удалось получить остатки Ozon'))
      return
    }

    const data: OzonStock[] = await response.json()
    setOzonStocks(data)
    setStockStatus(`Получено товаров с остатками: ${data.length}`)
    setEditingPrices(
      data.reduce<Record<number, string>>((acc, item) => {
        acc[item.productId] = String(item.price)
        return acc
      }, {}),
    )
  }

  async function updateOzonPrice(item: OzonStock) {
    const price = Number(editingPrices[item.productId]?.replace(',', '.'))
    if (!Number.isFinite(price) || price <= 0) {
      setPriceStatus('Введите корректную цену')
      return
    }

    setPriceStatus(`Отправляем цену для ${item.offerId}...`)
    const response = await fetch('/api/ozon/prices', {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        productId: item.productId,
        offerId: item.offerId,
        price,
        oldPrice: item.oldPrice,
        minPrice: item.minPrice,
        currencyCode: item.currencyCode || 'KZT',
      }),
    })

    if (!response.ok) {
      const errorText = await response.text()
      setPriceStatus(getApiErrorMessage(errorText, 'Не удалось изменить цену в Ozon'))
      return
    }

    const result: { success?: boolean; message?: string } = await response.json()
    if (result.success === false) {
      setPriceStatus(result.message || 'Ozon не принял новую цену')
      return
    }

    setUrgentStockStatus('')
    setStockStatus('')
    setPriceStatus(result.message || `Цена для ${item.offerId} успешно обновлена в Ozon`)
    setEditingPrices((current) => ({
      ...current,
      [item.productId]: String(price),
    }))
    setOzonStocks((current) =>
      current.map((stock) => (stock.productId === item.productId ? { ...stock, price } : stock)),
    )
  }

  async function loadAnalytics() {
    setAnalyticsStatus('Загружаем аналитику Ozon...')

    const response = await fetch('/api/ozon/analytics', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setAnalyticsStatus(getApiErrorMessage(await response.text(), 'Не удалось получить аналитику Ozon'))
      return
    }

    const data: OzonAnalytics = await response.json()
    setAnalytics(data)
    setAnalyticsStatus(`Аналитика обновлена: ${data.timestamp}`)
  }

  async function checkUrgentStocks() {
    setUrgentStockStatus('Анализируем остатки и продажи Ozon...')
    setStockStatus('Загружаем свежие остатки и продажи для срочного пополнения...')

    const [stocksResponse, analyticsResponse] = await Promise.all([
      fetch('/api/ozon/stocks', {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      }),
      fetch('/api/ozon/analytics', {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      }),
    ])

    if (!stocksResponse.ok) {
      const message = getApiErrorMessage(await stocksResponse.text(), 'Не удалось получить остатки Ozon')
      setStockStatus(message)
      setUrgentStockStatus(message)
      return
    }

    if (!analyticsResponse.ok) {
      const message = getApiErrorMessage(await analyticsResponse.text(), 'Не удалось получить продажи Ozon')
      setStockStatus(message)
      setUrgentStockStatus(message)
      return
    }

    const stocksData: OzonStock[] = await stocksResponse.json()
    const analyticsData: OzonAnalytics = await analyticsResponse.json()
    const urgentItems = getUrgentStockItems(stocksData, analyticsData).filter((item) =>
      purchaseProducts.some((product) => product.productId === item.productId),
    )

    setOzonStocks(stocksData)
    setAnalytics(analyticsData)
    setEditingPrices(
      stocksData.reduce<Record<number, string>>((acc, item) => {
        acc[item.productId] = String(item.price)
        return acc
      }, {}),
    )
    setUrgentStockOnly(urgentItems.length > 0)
    setUrgentStockResults(urgentItems)
    setShowUrgentStockModal(true)
    setUrgentStockStatus(
      urgentItems.length > 0
        ? `Срочно пополнить: ${urgentItems.length}. Эти товары закончатся за 5 дней или раньше.`
        : 'Всех товаров хватает: позиций, которые закончатся за 5 дней, нет.',
    )
    setStockStatus(
      urgentItems.length > 0
        ? `Найдено срочных товаров: ${urgentItems.length}`
        : 'Всех товаров хватает',
    )
  }

  async function createSupplyFromUrgentStocks() {
    if (urgentStockResults.length === 0) {
      return
    }

    setSupplyStatus('Создаем поставку из срочных товаров...')
    const response = await fetch('/api/supplies', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        items: urgentStockResults.map((item) => ({
          ozonProductId: item.productId,
          offerId: item.offerId,
          productName: item.name,
          quantity: getUrgentSupplyQuantity(item),
          isReserve: false,
        })),
      }),
    })

    if (!response.ok) {
      setSupplyStatus(getApiErrorMessage(await response.text(), 'Не удалось создать поставку из срочных товаров'))
      return
    }

    await loadSupplies()
    setShowUrgentStockModal(false)
    setActiveTab('supplies')
    setSupplySubTab('create')
    setSupplyStatus('Поставка из срочных товаров создана')
  }

  async function loadProductionFiles(search: string) {
    const params = new URLSearchParams()
    if (search.trim()) {
      params.set('search', search.trim())
    }

    const response = await fetch(`/api/production/files?${params.toString()}`, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setProductionStatus('Не удалось загрузить данные производства')
      return
    }

    const data: ProductionFile[] = await response.json()
    setProductionFiles(data)
    setProductionStatus(data.length ? `Найдено записей: ${data.length}` : 'Записей пока нет')
  }

  async function uploadProductionFile(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!uploadFile) {
      setProductionStatus('Выберите файл')
      return
    }

    if (!selectedProductionProduct) {
      setProductionStatus('Выберите товар')
      return
    }

    const formData = new FormData()
    formData.append('ozonProductId', String(selectedProductionProduct.productId))
    formData.append('offerId', selectedProductionProduct.offerId)
    formData.append('productName', selectedProductionProduct.name)
    formData.append('notes', '')
    formData.append('file', uploadFile)

    const response = await fetch('/api/production/files', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
      },
      body: formData,
    })

    if (!response.ok) {
      setProductionStatus('Не удалось загрузить файл')
      return
    }

    setUploadFile(null)
    setProductionStatus('Файл загружен')
    await loadProductionFiles(productionSearch)
  }

  async function downloadProductionFile(id: string) {
    const response = await fetch(`/api/production/files/${id}/download`, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setProductionStatus('Не удалось скачать файл')
      return
    }

    const blob = await response.blob()
    const contentDisposition = response.headers.get('content-disposition') ?? ''
    const fileNameMatch = contentDisposition.match(/filename\*=UTF-8''([^;]+)|filename="?([^"]+)"?/i)
    const fileName = decodeURIComponent(fileNameMatch?.[1] ?? fileNameMatch?.[2] ?? 'production-file')
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')

    link.href = url
    link.download = fileName
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
  }

  async function deleteProductionFile(id: string) {
    const response = await fetch(`/api/production/files/${id}`, {
      method: 'DELETE',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setProductionStatus('Не удалось удалить файл')
      return
    }

    setProductionStatus('Файл удален')
    await loadProductionFiles(productionSearch)
  }

  function markTaskNotificationsSeen(kind: 'new' | 'in-progress', taskIds: string[]) {
    if (!user?.id || taskIds.length === 0) {
      return
    }

    const storageKey = getTaskNotificationStorageKey(user.id, kind)
    const updateState = kind === 'new' ? setSeenNewTaskNotificationIds : setSeenInProgressTaskNotificationIds

    updateState((current) => {
      const next = Array.from(new Set([...current, ...taskIds]))
      localStorage.setItem(storageKey, JSON.stringify(next))
      return next
    })
  }

  function markChatNotificationsSeen(chatUserId: string) {
    setChatUsers((current) =>
      current.map((item) => (item.id === chatUserId ? { ...item, unreadCount: 0 } : item)),
    )
  }

  function markVisibleNotificationsSeen() {
    markTaskNotificationsSeen('new', unseenNewProductionTasks.map((task) => task.id))
    markTaskNotificationsSeen('in-progress', unseenInProgressProductionTasks.map((task) => task.id))
    chatUsers
      .filter((item) => (item.unreadCount ?? 0) > 0)
      .forEach((item) => markChatNotificationsSeen(item.id))
  }

  async function loadProductionTasks() {
    const response = await fetch('/api/production/tasks', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setTaskStatus('Не удалось загрузить задачи')
      return
    }

    const data: ProductionTask[] = await response.json()
    const newTasks = data.filter((task) => task.status === 'New')
    const previousTaskIds = knownNewTaskIdsRef.current
    const nextTaskIds = new Set(newTasks.map((task) => task.id))

    if (previousTaskIds) {
      const arrivedTasks = newTasks.filter((task) => !previousTaskIds.has(task.id))
      if (arrivedTasks.length > 0) {
        showBrowserNotification(
          'Новая задача',
          arrivedTasks.length === 1
            ? getProductionTaskSummary(arrivedTasks[0])
            : `Новых задач: ${arrivedTasks.length}`,
        )
      }
    }

    knownNewTaskIdsRef.current = nextTaskIds
    setProductionTasks(data)
    setTaskStatus(data.length ? `Задач: ${data.length}` : 'Задач пока нет')
  }

  function addDraftTaskItem() {
    const product = productionProducts.find((item) => String(item.productId) === selectedTaskProductId)
    const quantity = Number(taskQuantity)

    if (!product || !Number.isFinite(quantity) || quantity <= 0) {
      setTaskStatus('Выберите товар и укажите количество')
      return
    }

    setDraftTaskItems((current) => [
      ...current,
      {
        tempId: createTempId(),
        ozonProductId: product.productId,
        offerId: product.offerId,
        productName: product.name,
        imageUrl: product.imageUrl,
        requiredQuantity: quantity,
      },
    ])
    setSelectedTaskProductId('')
    setTaskQuantity('')
    setTaskStatus('Товар добавлен в задачу')
  }

  async function createProductionTasksFromDraft() {
    if (draftTaskItems.length === 0) {
      setTaskStatus('Добавьте хотя бы один товар')
      return
    }

    const response = await fetch('/api/production/tasks', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        items: draftTaskItems.map((item) => ({
          ozonProductId: item.ozonProductId,
          offerId: item.offerId,
          productName: item.productName,
          requiredQuantity: item.requiredQuantity,
        })),
      }),
    })

    if (!response.ok) {
      setTaskStatus('Не удалось создать задачу')
      return
    }

    setDraftTaskItems([])
    setShowCreateTaskModal(false)
    setTaskStatus('Задача создана')
    await loadProductionTasks()
  }

  async function startProductionTask(id: string) {
    const response = await fetch(`/api/production/tasks/${id}/start`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setTaskStatus('Не удалось взять задачу в работу')
      return
    }

    setTaskStatus('Задача взята в работу')
    await loadProductionTasks()
  }

  async function deferProductionTask(id: string) {
    const response = await fetch(`/api/production/tasks/${id}/defer`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setTaskStatus('Не удалось отложить задачу')
      return
    }

    setTaskStatus('Задача отложена')
    await loadProductionTasks()
  }

  async function completeProductionTask(id: string) {
    const task = productionTasks.find((item) => item.id === id)
    const taskItems = task ? getProductionTaskItems(task) : []
    const completedItems = taskItems.map((item) => ({
      id: item.id,
      actualQuantity: Number(actualQuantities[item.id]),
    }))

    if (
      completedItems.length === 0 ||
      completedItems.some((item) => !Number.isFinite(item.actualQuantity) || item.actualQuantity < 0)
    ) {
      setTaskStatus('Укажите фактическое количество по каждому товару')
      return
    }

    const actualQuantity = completedItems.reduce((sum, item) => sum + item.actualQuantity, 0)

    const response = await fetch(`/api/production/tasks/${id}/complete`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ actualQuantity, items: completedItems }),
    })

    if (!response.ok) {
      setTaskStatus('Не удалось завершить задачу')
      return
    }

    setActualQuantities((current) => {
      const next = { ...current }
      taskItems.forEach((item) => {
        next[item.id] = ''
      })
      return next
    })
    setTaskStatus('Задача выполнена')
    await loadProductionTasks()
  }

  async function archiveProductionTask(id: string) {
    const response = await fetch(`/api/production/tasks/${id}/archive`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      const message = await response.text()
      setTaskStatus(message || 'Не удалось архивировать задачу')
      return
    }

    setTaskStatus('Задача отправлена в архив')
    await loadProductionTasks()
  }

  async function deleteProductionTask(id: string) {
    if (!window.confirm('Удалить задачу из архива без возможности восстановления?')) {
      return
    }

    const response = await fetch(`/api/production/tasks/${id}`, {
      method: 'DELETE',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setTaskStatus('Не удалось удалить задачу')
      return
    }

    setTaskStatus('Задача удалена')
    await loadProductionTasks()
  }

  async function loadSupplies() {
    const response = await fetch('/api/supplies', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setSupplyStatus('Не удалось загрузить поставки')
      return
    }

    const data: Supply[] = await response.json()
    setSupplies(data)
    setSupplyStatus(data.length ? `Поставок: ${data.length}` : 'Поставок пока нет')
  }

  async function loadSupplyAnalytics() {
    const response = await fetch('/api/supplies/analytics', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setSupplyStatus('Не удалось загрузить аналитику поставок')
      return
    }

    const data: SupplyAnalyticsItem[] = await response.json()
    setSupplyAnalytics(data)
  }

  function addSupplyProduct() {
    const product = purchaseProducts.find((item) => String(item.productId) === supplyProductId)
    const quantity = Number(supplyQuantity)

    if (!product || !Number.isFinite(quantity) || quantity <= 0) {
      setSupplyStatus('Выберите товар и укажите количество')
      return
    }

    setDraftSupplyItems((current) => [
      ...current,
      {
        tempId: createTempId(),
        ozonProductId: product.productId,
        offerId: product.offerId,
        productName: product.name,
        quantity,
        actualOrderQuantity: 0,
        supplierName: '',
        supplierUrl: '',
        isReserve: false,
      },
    ])
    setSupplyProductId('')
    setSupplyQuantity('')
    setSupplyStatus('Товар добавлен в поставку')
  }

  function addReserveSupplyProduct() {
    const quantity = Number(reserveQuantity)

    if (!reserveProductName.trim() || !Number.isFinite(quantity) || quantity <= 0) {
      setSupplyStatus('Укажите название резервного товара и количество')
      return
    }

    if (Boolean(reserveSupplierName.trim()) !== Boolean(reserveSupplierUrl.trim())) {
      setSupplyStatus('Для поставщика резервного товара укажите название и ссылку')
      return
    }

    setDraftSupplyItems((current) => [
      ...current,
      {
        tempId: createTempId(),
        offerId: '',
        productName: reserveProductName.trim(),
        quantity,
        actualOrderQuantity: 0,
        supplierName: reserveSupplierName.trim(),
        supplierUrl: reserveSupplierUrl.trim(),
        isReserve: true,
      },
    ])
    setReserveProductName('')
    setReserveQuantity('')
    setReserveSupplierName('')
    setReserveSupplierUrl('')
    setSupplyStatus('Резервный товар добавлен')
  }

  async function createSupply() {
    if (draftSupplyItems.length === 0) {
      setSupplyStatus('Добавьте товары в поставку')
      return
    }

    const response = await fetch('/api/supplies', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        items: draftSupplyItems.map(({ tempId: _tempId, ...item }) => item),
      }),
    })

    if (!response.ok) {
      const message = await response.text()
      setSupplyStatus(message || 'Не удалось создать поставку')
      return
    }

    setDraftSupplyItems([])
    setShowCreateSupplyModal(false)
    setSupplyStatus('Поставка создана со статусом "Создано"')
    await loadSupplies()
    if (isAdminLike) {
      await loadSupplyAnalytics()
    }
  }

  async function downloadSupplyTemplate() {
    const response = await fetch('/api/supplies/import-template', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setSupplyStatus('Не удалось скачать шаблон')
      return
    }

    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = 'supply-template.xlsx'
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
  }

  async function downloadSupplyOrder(id: string) {
    const response = await fetch(`/api/supplies/${id}/order-export`, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setSupplyStatus(getApiErrorMessage(await response.text(), 'Не удалось скачать таблицу заказа'))
      return
    }

    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')

    link.href = url
    link.download = `supply-order-${id}.xlsx`
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
    setSupplyStatus('Таблица заказа скачана')
  }

  async function uploadSupplyExcel() {
    if (!supplyImportFile) {
      setSupplyStatus('Выберите Excel-файл')
      return
    }

    const formData = new FormData()
    formData.append('file', supplyImportFile)

    const response = await fetch('/api/supplies/import', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
      },
      body: formData,
    })

    if (!response.ok) {
      const message = await response.text()
      setSupplyStatus(message || 'Не удалось импортировать Excel')
      return
    }

    const result = await response.json()
    setSupplyImportFile(null)
    setSupplyStatus(`Поставка создана из Excel. Строк: ${result.items}`)
    await loadSupplies()
    await loadSupplyAnalytics()
  }

  async function updateSupplyStatus(id: string, status: SupplyStatus) {
    if (
      status === 'Sent' &&
      !window.confirm('Подтвердите отправку поставки. После этого обычный пользователь уже не сможет ее редактировать.')
    ) {
      return
    }

    const response = await fetch(`/api/supplies/${id}/status`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ status }),
    })

    if (!response.ok) {
      const message = await response.text()
      setSupplyStatus(message || 'Не удалось сохранить статус поставки')
      return
    }

    setSupplyStatus('Статус поставки сохранен')
    await loadSupplies()
    if (isAdminLike) {
      await loadSupplyAnalytics()
    }
  }

  async function updateSupplyItemActualOrder(itemId: string, fallbackQuantity: number) {
    const value = Number(supplyActualOrderEdits[itemId] ?? fallbackQuantity)
    if (!Number.isFinite(value) || value < 0) {
      setSupplyStatus('Факт заказа должен быть числом не меньше нуля')
      return
    }

    const response = await fetch(`/api/supply-items/${itemId}/actual-order`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ actualOrderQuantity: value }),
    })

    if (!response.ok) {
      const message = await response.text()
      setSupplyStatus(message || 'Не удалось сохранить факт заказа')
      return
    }

    setSupplyActualOrderEdits((current) => {
      const next = { ...current }
      delete next[itemId]
      return next
    })
    setSupplyStatus('Факт заказа сохранен')
    await loadSupplies()
    await loadProductSupplierLinks()
    if (isAdminLike) {
      await loadSupplyAnalytics()
    }
  }

  function startEditSupply(supply: Supply) {
    setEditingSupplyId(supply.id)
    setEditSupplyItems(
      supply.items.map((item) => ({
        tempId: item.id,
        id: item.id,
        ozonProductId: item.ozonProductId,
        offerId: item.offerId,
        productName: item.productName,
        quantity: item.quantity,
        actualOrderQuantity: item.actualOrderQuantity,
        supplierName: item.supplierName,
        supplierUrl: item.supplierUrl,
        isReserve: item.isReserve,
      })),
    )
    setEditSupplyProductId('')
    setEditSupplyQuantity('')
    setEditReserveProductName('')
    setEditReserveQuantity('')
    setEditReserveSupplierName('')
    setEditReserveSupplierUrl('')
  }

  function cancelEditSupply() {
    setEditingSupplyId(null)
    setEditSupplyItems([])
    setEditSupplyProductId('')
    setEditSupplyQuantity('')
    setEditReserveProductName('')
    setEditReserveQuantity('')
    setEditReserveSupplierName('')
    setEditReserveSupplierUrl('')
  }

  function addEditSupplyProduct() {
    const product = purchaseProducts.find((item) => String(item.productId) === editSupplyProductId)
    const quantity = Number(editSupplyQuantity)

    if (!product || !Number.isFinite(quantity) || quantity <= 0) {
      setSupplyStatus('Выберите товар и укажите количество')
      return
    }

    setEditSupplyItems((current) => [
      ...current,
      {
        tempId: createTempId(),
        ozonProductId: product.productId,
        offerId: product.offerId,
        productName: product.name,
        quantity,
        actualOrderQuantity: 0,
        supplierName: '',
        supplierUrl: '',
        isReserve: false,
      },
    ])
    setEditSupplyProductId('')
    setEditSupplyQuantity('')
  }

  function addEditReserveSupplyProduct() {
    const quantity = Number(editReserveQuantity)

    if (!editReserveProductName.trim() || !Number.isFinite(quantity) || quantity <= 0) {
      setSupplyStatus('Укажите название резервного товара и количество')
      return
    }

    if (Boolean(editReserveSupplierName.trim()) !== Boolean(editReserveSupplierUrl.trim())) {
      setSupplyStatus('Для поставщика резервного товара укажите название и ссылку')
      return
    }

    setEditSupplyItems((current) => [
      ...current,
      {
        tempId: createTempId(),
        offerId: '',
        productName: editReserveProductName.trim(),
        quantity,
        actualOrderQuantity: 0,
        supplierName: editReserveSupplierName.trim(),
        supplierUrl: editReserveSupplierUrl.trim(),
        isReserve: true,
      },
    ])
    setEditReserveProductName('')
    setEditReserveQuantity('')
    setEditReserveSupplierName('')
    setEditReserveSupplierUrl('')
  }

  async function saveSupplyEdit(id: string) {
    if (editSupplyItems.length === 0) {
      setSupplyStatus('В поставке должен быть хотя бы один товар')
      return
    }

    if (editSupplyItems.some((item) => Boolean(item.supplierName.trim()) !== Boolean(item.supplierUrl.trim()))) {
      setSupplyStatus('Для поставщика укажите название и ссылку')
      return
    }

    const response = await fetch(`/api/supplies/${id}`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        items: editSupplyItems.map(({ tempId: _tempId, id: _id, ...item }) => item),
      }),
    })

    if (!response.ok) {
      const message = await response.text()
      setSupplyStatus(message || 'Не удалось сохранить поставку')
      return
    }

    await loadSupplies()
    await loadProductSupplierLinks()
    await loadSupplyAnalytics()
    cancelEditSupply()
    setSupplyStatus('Поставка сохранена')
  }

  async function archiveSupply(id: string) {
    const response = await fetch(`/api/supplies/${id}/archive`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      const message = await response.text()
      setSupplyStatus(message || 'Не удалось архивировать поставку')
      return
    }

    if (editingSupplyId === id) {
      cancelEditSupply()
    }
    setSupplyStatus('Поставка отправлена в архив')
    await loadSupplies()
    await loadSupplyAnalytics()
  }

  async function deleteSupply(id: string) {
    if (!window.confirm('Удалить поставку из архива без возможности восстановления?')) {
      return
    }

    const response = await fetch(`/api/supplies/${id}`, {
      method: 'DELETE',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    if (!response.ok) {
      setSupplyStatus('Не удалось удалить поставку')
      return
    }

    if (editingSupplyId === id) {
      cancelEditSupply()
    }
    setSupplyStatus('Поставка удалена')
    await loadSupplies()
    await loadSupplyAnalytics()
  }

  if (!token) {
    const authTitle =
      authMode === 'register'
        ? 'Регистрация компании'
        : authMode === 'forgot'
          ? 'Сброс пароля'
          : authMode === 'reset'
            ? 'Новый пароль'
            : 'Вход в панель'
    const authSubmit =
      authMode === 'register'
        ? handleRegister
        : authMode === 'forgot'
          ? handlePasswordResetRequest
          : authMode === 'reset'
            ? handlePasswordResetConfirm
            : handleLogin

    return (
      <main className="login-page">
        <form className="login-form" onSubmit={authSubmit}>
          <img className="login-logo" src={fulveroLogo} alt="Fulvero" />
          <h1>{authTitle}</h1>
          {authMode !== 'reset' && (
            <div className="auth-switch">
              <button
                type="button"
                className={authMode === 'login' ? 'active' : ''}
                onClick={() => {
                  setAuthMode('login')
                  setLoginError('')
                  setPasswordResetStatus('')
                }}
              >
                Вход
              </button>
              <button
                type="button"
                className={authMode === 'register' ? 'active' : ''}
                onClick={() => {
                  setAuthMode('register')
                  setLoginError('')
                  setPasswordResetStatus('')
                }}
              >
                Регистрация
              </button>
            </div>
          )}
          {authMode === 'reset' ? (
            <>
              <p className="trial-note">Задайте новый пароль администратора. Ссылка из письма одноразовая.</p>
              <label>
                Новый пароль
                <input name="password" type="password" autoComplete="new-password" required minLength={6} />
              </label>
              <label>
                Повторите пароль
                <input name="passwordRepeat" type="password" autoComplete="new-password" required minLength={6} />
              </label>
            </>
          ) : (
            <>
              <label>
                Компания
                <input name="companyName" autoComplete="organization" required />
              </label>
              {authMode === 'register' && (
                <label>
                  Имя первого сотрудника
                  <input name="displayName" autoComplete="name" required />
                </label>
              )}
              {authMode === 'register' && (
                <label>
                  Email
                  <input name="email" type="email" autoComplete="email" required />
                </label>
              )}
              <label>
                {authMode === 'login' || authMode === 'forgot' ? 'Логин или Email администратора' : 'Логин'}
                <input name="userName" autoComplete="username" required />
              </label>
              {authMode !== 'forgot' && (
                <label>
                  Пароль
                  <input name="password" type="password" autoComplete={authMode === 'login' ? 'current-password' : 'new-password'} required />
                </label>
              )}
            </>
          )}
          {loginError && <p className="error">{loginError}</p>}
          {passwordResetStatus && <p className="success">{passwordResetStatus}</p>}
          <button type="submit">
            {authMode === 'register'
              ? 'Создать компанию и админа'
              : authMode === 'forgot'
                ? 'Отправить ссылку'
                : authMode === 'reset'
                  ? 'Сохранить новый пароль'
                  : 'Войти'}
          </button>
          {authMode === 'login' && (
            <button
              type="button"
              className="secondary-action"
              onClick={() => {
                setAuthMode('forgot')
                setLoginError('')
                setPasswordResetStatus('')
              }}
            >
              Сбросить пароль
            </button>
          )}
          {(authMode === 'forgot' || authMode === 'reset') && (
            <button
              type="button"
              className="secondary-action"
              onClick={() => {
                setAuthMode('login')
                setLoginError('')
                setPasswordResetStatus('')
                setPasswordResetToken('')
                window.history.replaceState(null, '', window.location.pathname)
              }}
            >
              Вернуться ко входу
            </button>
          )}
          {authMode === 'register' && (
            <p className="trial-note">Демо-доступ включится на 3 дня. Первый сотрудник сразу станет администратором.</p>
          )}
          {authMode === 'forgot' && (
            <p className="trial-note">Сброс доступен только администраторам компании. Сотрудникам пароль меняет админ.</p>
          )}
        </form>
      </main>
    )
  }

  if (user && !user.hasActiveSubscription) {
    const canPaySubscription = isAdminLike

    return (
      <main className="login-page">
        <section className="login-form subscription-gate">
          <img className="login-logo" src={fulveroLogo} alt="Fulvero" />
          <h1>Нужна подписка</h1>
          <p>
            Демо-доступ компании <strong>{user.companyName}</strong> закончился. Чтобы продолжить работу,
            нужна ежемесячная подписка через ЮKassa.
          </p>
          {canPaySubscription ? (
            <>
              <button type="button" onClick={startSubscriptionCheckout}>
                Оплатить подписку 5 900 ₽
              </button>
              <p className="trial-note">После оплаты доступ компании продлится на 30 дней.</p>
            </>
          ) : (
            <p className="trial-note">
              Оплатить подписку может только администратор компании. Обратитесь к администратору.
            </p>
          )}
          <button type="button" className="secondary-action" onClick={logout}>
            Выйти
          </button>
          {billingStatus && <p className="trial-note">{billingStatus}</p>}
        </section>
      </main>
    )
  }

  return (
    <main className="app-layout">
      <header className="app-header">
        <div className="brand">
          <img src={fulveroLogo} alt="Fulvero" />
          <span className="company-name">{user?.companyName || 'Компания'}</span>
        </div>

        <nav className="main-tabs" aria-label="Основные разделы">
            {visibleTabs.map((tab) => (
              <button
                key={tab.id}
                type="button"
                className={activeTab === tab.id ? 'active' : ''}
                onClick={() => setActiveTab(tab.id)}
              >
                {tab.label}
                {tab.id === 'production' && productionNotificationTotal > 0 && (
                  <span className="tab-badge">{productionNotificationTotal}</span>
                )}
                {tab.id === 'chats' && chatUnreadTotal > 0 && (
                  <span className="tab-badge">{chatUnreadTotal}</span>
                )}
              </button>
            ))}
        </nav>

        <div className="session">
          <div className="notification-menu">
            <button
              type="button"
              className="notification-button"
              aria-label="Уведомления"
              onClick={() => {
                if (showNotifications) {
                  markVisibleNotificationsSeen()
                }
                setShowNotifications((current) => !current)
              }}
            >
              <svg viewBox="0 0 24 24" aria-hidden="true">
                <path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9" />
                <path d="M10 21h4" />
              </svg>
              {notificationTotal > 0 && <span className="notification-badge">{notificationTotal}</span>}
            </button>
            {showNotifications && (
              <div className="notification-panel">
                {notificationItems.map((item) => (
                  <button
                    type="button"
                    key={item.key}
                    onClick={() => {
                      setShowNotifications(false)
                      if (item.target === 'chat') {
                        setSelectedChatUserId(item.userId)
                        markChatNotificationsSeen(item.userId)
                        setActiveTab('chats')
                      } else if (item.target === 'tasks') {
                        markTaskNotificationsSeen('new', allNewProductionTasks.map((task) => task.id))
                        setActiveTab('production')
                        setProductionSubTab('tasks')
                      } else {
                        markTaskNotificationsSeen('in-progress', allInProgressProductionTasks.map((task) => task.id))
                        setActiveTab('production')
                        setProductionSubTab(item.target)
                      }
                    }}
                  >
                    {item.label}
                  </button>
                ))}
                {notificationItems.length === 0 && <span>Новых уведомлений нет</span>}
              </div>
            )}
          </div>
          <span className="session-user">
            <strong>{user?.displayName || user?.userName}</strong>
            {user?.position && <small>{user.position}</small>}
          </span>
          <button type="button" className="profile-button" onClick={() => setShowProfileModal(true)}>
            {user?.avatarUrl ? <img src={user.avatarUrl} alt="" /> : <span className="default-avatar" aria-hidden="true" />}
            <span className="presence-dot is-online" aria-hidden="true" />
          </button>
          <button type="button" className="logout-button" onClick={confirmLogout}>
            Выйти
          </button>
        </div>
      </header>

      {user?.isImpersonating && (
        <section className="impersonation-banner">
          <span>Вы работаете от имени компании {user.impersonatedCompanyName || user.companyName}</span>
          <button type="button" onClick={returnToFulvero}>
            Вернуться в Fulvero
          </button>
        </section>
      )}

      {showProfileModal && (
        <div className="modal-backdrop" role="presentation">
          <div className="modal-card" role="dialog" aria-modal="true">
            <div className="modal-title-row">
              <h3>Моя карточка</h3>
              <button type="button" onClick={() => setShowProfileModal(false)}>
                Закрыть
              </button>
            </div>
            <div className="profile-card">
              <label className="profile-avatar profile-avatar-upload">
                {user?.avatarUrl ? <img src={user.avatarUrl} alt="" /> : <span>Загрузить фото</span>}
                <input type="file" accept="image/*" onChange={handleProfileAvatarChange} />
              </label>
              <span className="profile-card-details">
                <strong>{user?.displayName || user?.userName}</strong>
                <small>ID сотрудника: {user?.id}</small>
                <small>{user?.position || 'Должность указывает администратор'}</small>
                {profileAvatar && <small>Выбрано: {profileAvatar.name}</small>}
              </span>
            </div>
            <form className="profile-form" onSubmit={saveProfile}>
              <input
                placeholder="Имя"
                value={profileForm.displayName}
                onChange={(event) => setProfileForm({ ...profileForm, displayName: event.target.value })}
                required
              />
              <span className="profile-actions">
                <button type="submit">Сохранить имя</button>
                <button type="button" onClick={uploadProfileAvatar}>
                  Сохранить фото
                </button>
              </span>
            </form>
            {profileStatus && <p className="modal-status">{profileStatus}</p>}
          </div>
        </div>
      )}

      {supplierModalProduct && (
        <div className="modal-backdrop" role="presentation">
          <div className="modal-card modal-card-wide" role="dialog" aria-modal="true">
            <div className="modal-title-row">
              <div>
                <h3>Поставщики товара</h3>
                <p>{supplierModalProduct.offerId} · {supplierModalProduct.name}</p>
              </div>
              <button type="button" onClick={() => setSupplierModalProduct(null)}>
                Закрыть
              </button>
            </div>

            <div className="supplier-editor">
              {supplierDrafts.map((item) => (
                <div className="supplier-editor-row" key={item.tempId}>
                  {item.isEditing ? (
                    <>
                      <label>
                        <span>Название поставщика</span>
                        <input
                          value={item.supplierName}
                          placeholder="Например: ИП Иванов"
                          onChange={(event) =>
                            setSupplierDrafts((current) =>
                              current.map((row) =>
                                row.tempId === item.tempId
                                  ? { ...row, supplierName: event.target.value }
                                  : row,
                              ),
                            )
                          }
                        />
                      </label>
                      <label>
                        <span>Ссылка на заказ</span>
                        <input
                          value={item.supplierUrl}
                          placeholder="https://..."
                          onChange={(event) =>
                            setSupplierDrafts((current) =>
                              current.map((row) =>
                                row.tempId === item.tempId
                                  ? { ...row, supplierUrl: event.target.value }
                                  : row,
                              ),
                            )
                          }
                        />
                      </label>
                    </>
                  ) : (
                    <span className="supplier-saved-link">
                      <strong>{item.supplierName || 'Поставщик'}</strong>
                      {item.supplierUrl ? (
                        <a href={item.supplierUrl} target="_blank" rel="noreferrer">
                          Открыть сайт
                        </a>
                      ) : (
                        <small>Ссылка не указана</small>
                      )}
                    </span>
                  )}
                  {!item.isEditing && (
                    <button
                      type="button"
                      onClick={() =>
                        setSupplierDrafts((current) =>
                          current.map((row) =>
                            row.tempId === item.tempId ? { ...row, isEditing: true } : row,
                          ),
                        )
                      }
                    >
                      Изменить
                    </button>
                  )}
                  <button
                    type="button"
                    className="tiny-danger"
                    onClick={() =>
                      setSupplierDrafts((current) => current.filter((row) => row.tempId !== item.tempId))
                    }
                  >
                    Удалить
                  </button>
                </div>
              ))}
              {supplierDrafts.length === 0 && (
                <div className="empty-state">
                  <strong>Поставщики еще не добавлены.</strong>
                </div>
              )}
            </div>

            <div className="supply-actions">
              <button type="button" onClick={addSupplierDraft}>
                Добавить поставщика
              </button>
              <button type="button" onClick={saveSupplierLinks}>
                Сохранить поставщиков
              </button>
            </div>
          </div>
        </div>
      )}

      {productTypeModalProduct && (
        <div className="modal-backdrop" role="presentation">
          <div className="modal-card" role="dialog" aria-modal="true">
            <div className="modal-title-row">
              <div>
                <h3>Смена типа товара</h3>
                <p>{productTypeModalProduct.offerId} · {productTypeModalProduct.name}</p>
              </div>
              <button type="button" onClick={() => setProductTypeModalProduct(null)}>
                Закрыть
              </button>
            </div>

            <div className="product-type-picker">
              <button
                type="button"
                className={productTypeDraft === 'Production' ? 'active' : ''}
                onClick={() => setProductTypeDraft('Production')}
              >
                Производственный
              </button>
              <button
                type="button"
                className={productTypeDraft === 'Purchase' ? 'active' : ''}
                onClick={() => setProductTypeDraft('Purchase')}
              >
                Закупочный
              </button>
            </div>

            <div className="supply-actions">
              <button type="button" onClick={saveProductTypeFromModal}>
                Сохранить
              </button>
            </div>
          </div>
        </div>
      )}

      {showUrgentStockModal && (
        <div className="modal-backdrop" role="presentation">
          <div className="modal-card urgent-modal" role="dialog" aria-modal="true">
            <div className="modal-title-row">
              <h3>Срочное пополнение склада</h3>
              <button type="button" onClick={() => setShowUrgentStockModal(false)}>
                Закрыть
              </button>
            </div>
            {urgentStockResults.length > 0 ? (
              <div className="urgent-stock-list">
                {urgentStockResults.map((item) => (
                  <article key={item.productId}>
                    <strong>{item.name}</strong>
                    <span>{item.offerId}</span>
                    <small>
                      Остаток FBO+FBS: {item.fboPresent + item.fbsPresent}. Хватит на{' '}
                      {formatDaysLeft(item.daysLeft)} при продажах {item.dailySales.toFixed(2)}/день.
                      В поставку: {getUrgentSupplyQuantity(item)} шт.
                    </small>
                  </article>
                ))}
                <button type="button" className="danger-action" onClick={createSupplyFromUrgentStocks}>
                  В поставку
                </button>
              </div>
            ) : (
              <div className="empty-state">
                <strong>Все хорошо, всего хватает.</strong>
                <p>Товаров, которые закончатся за 5 дней или раньше, нет.</p>
              </div>
            )}
          </div>
        </div>
      )}

      <div className="app-content">
        <section className="workspace">
          {activeTab === 'dashboard' && (
            <section className="dashboard-page">
              <div className="section-title">
                <h2>Главная</h2>
                <p>Сводка по товарам, производству, поставкам и продажам</p>
              </div>

              <div className="dashboard-grid">
                <article className="dashboard-card dashboard-card-wide attention-card">
                  <div className="dashboard-card-head">
                    <span>
                      <strong>Требует внимания</strong>
                      <small>Самые срочные действия</small>
                    </span>
                  </div>
                  <div className="attention-list">
                    <span><b>{urgentPurchaseItems.length}</b><small>срочно закупить</small></span>
                    <span><b>{urgentProductionItems.length}</b><small>произвести</small></span>
                    <span><b>{urgentStockItems.length}</b><small>скоро закончится</small></span>
                    <span><b>{unsetProducts.length}</b><small>без типа</small></span>
                  </div>
                  <div className="dashboard-actions">
                    <button
                      type="button"
                      onClick={() => {
                        setActiveTab('production')
                        setProductionSubTab('tasks')
                        setShowCreateTaskModal(true)
                      }}
                    >
                      Создать производство
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        setActiveTab('supplies')
                        setSupplySubTab('create')
                        setShowCreateSupplyModal(true)
                      }}
                    >
                      Создать заказ
                    </button>
                  </div>
                </article>

                <article className="dashboard-card">
                  <div className="dashboard-card-head">
                    <span><strong>Производство</strong><small>Задачи по товарам</small></span>
                  </div>
                  <div className="metric-list">
                    <span><b>{allNewProductionTasks.length}</b><small>новые</small></span>
                    <span><b>{allInProgressProductionTasks.length}</b><small>в работе</small></span>
                    <span><b>{completedProductionTasks.length}</b><small>готово</small></span>
                  </div>
                  <button type="button" onClick={() => setActiveTab('production')}>Открыть производство</button>
                </article>

                <article className="dashboard-card dashboard-card-with-new">
                  <div className="dashboard-card-head">
                    <span><strong>Поставки</strong><small>Заказы и приемка</small></span>
                  </div>
                  <div className="metric-list">
                    <span><b>{createdSupplies.length}</b><small>создано</small></span>
                    <span><b>{activeSupplies.filter((item) => item.status === 'Sent').length}</b><small>в пути</small></span>
                    <span><b>{activeSupplies.filter((item) => item.status === 'Accepted').length}</b><small>принято</small></span>
                  </div>
                  <button type="button" onClick={() => setActiveTab('supplies')}>Открыть поставки</button>
                  <div className="dashboard-inline-new">
                    <div className="dashboard-card-head">
                      <span><strong>Новое</strong><small>Товары без типа</small></span>
                    </div>
                    <div className="new-products-list compact-new-products">
                      {unsetProducts.slice(0, 4).map((item) => (
                        <span key={item.productId}><b>{item.offerId}</b><small>{item.name}</small></span>
                      ))}
                      {unsetProducts.length === 0 && <small>Все товары распределены.</small>}
                    </div>
                    <button
                      type="button"
                      onClick={() => {
                        setActiveTab('products')
                        setProductsSubTab('settings')
                      }}
                    >
                      Настроить товары
                    </button>
                  </div>
                </article>

                <article className="dashboard-card dashboard-card-wide">
                  <div className="dashboard-card-head">
                    <span><strong>График продаж</strong><small>Реальные заказы Ozon за последние 30 дней</small></span>
                    <button type="button" className="compact-button" onClick={loadAnalytics}>Обновить</button>
                  </div>
                  <div className="chart-summary">
                    <span><b>{analytics?.orderedUnitsTotal ?? 0}</b><small>продано, шт.</small></span>
                    <span><b>{analytics ? formatPlainNumber(analytics.revenueTotal) : '-'}</b><small>выручка</small></span>
                    <span><b>{analytics?.deliveringCount ?? 0}</b><small>заказов в пути</small></span>
                    <span><b>{analytics?.awaitingPackagingCount ?? 0}</b><small>собираются</small></span>
                    <span><b>{analytics?.awaitingDeliverCount ?? 0}</b><small>готово к отгрузке</small></span>
                    <span><b>{analytics ? formatPlainNumber(analytics.cancelledUnitsTotal ?? 0) : '-'}</b><small>отменено, шт.</small></span>
                    <span><b>{analytics ? formatPlainNumber(analytics.accountBalance?.amount ?? 0) : '-'}</b><small>на счету сейчас</small></span>
                  </div>
                  <div className="sales-chart-shell">
                    <div className="sales-chart-scale left-scale" aria-hidden="true">
                      <span>{formatPlainNumber(maxDashboardSales)}</span>
                      <span>{formatPlainNumber(maxDashboardSales / 2)}</span>
                      <span>0</span>
                    </div>
                    <div className="sales-chart" aria-label="График реальных продаж за 30 дней">
                      {dashboardSalesDays.map((day, index) => (
                        <span className="sales-chart-day" key={day.date}>
                          <i
                            title={`${formatDateShort(day.date)}: ${formatPlainNumber(day.quantity)} шт., ${formatPlainNumber(day.revenue)}`}
                            style={{
                              height: `${Math.max(6, Math.round((day.quantity / maxDashboardSales) * 100))}%`,
                              opacity: day.quantity > 0 ? 1 : 0.28,
                            }}
                          />
                          {(index === 0 || index === dashboardSalesDays.length - 1 || index % 7 === 0) && (
                            <small>{formatDateShort(day.date)}</small>
                          )}
                        </span>
                      ))}
                    </div>
                    <div className="sales-chart-scale right-scale" aria-hidden="true">
                      <span>шт.</span>
                      <span>{formatPlainNumber(maxDashboardSales / 2)}</span>
                      <span>0</span>
                    </div>
                  </div>
                </article>
              </div>
            </section>
          )}

          {activeTab === 'production' && (
            <section className="tab-panel">
              <div className="section-title">
                <div>
                  <h2>Производство</h2>
                  <p>{productionSubTab === 'tasks' ? taskStatus : productionStatus || 'Фото, данные и задачи'}</p>
                </div>
                <span className="section-actions">
                  {productionSubTab === 'archive' && isAdminLike && hasSubFeature('production.archive', 'production') && (
                    <button type="button" className="header-action" onClick={exportTaskArchive}>
                      Скачать CSV
                    </button>
                  )}
                  <button
                    type="button"
                    className="header-action"
                    onClick={() => setProductionSubTab('archive')}
                    hidden={!hasSubFeature('production.archive', 'production')}
                  >
                    Архив задач
                  </button>
                </span>
              </div>

              <div className="inner-tabs">
                <button
                  type="button"
                  className={productionSubTab === 'products' ? 'active' : ''}
                  onClick={() => setProductionSubTab('products')}
                  hidden={!hasSubFeature('production.products', 'production')}
                >
                  Список товаров
                </button>
                <button
                  type="button"
                  className={productionSubTab === 'tasks' ? 'active' : ''}
                  onClick={() => {
                    markTaskNotificationsSeen('new', allNewProductionTasks.map((task) => task.id))
                    setProductionSubTab('tasks')
                  }}
                  hidden={!hasSubFeature('production.tasks', 'production')}
                >
                  Задачи
                  {unseenNewProductionTasks.length > 0 && (
                    <span className="tab-badge">{unseenNewProductionTasks.length}</span>
                  )}
                </button>
                <button
                  type="button"
                  className={productionSubTab === 'inProgress' ? 'active' : ''}
                  onClick={() => {
                    markTaskNotificationsSeen('in-progress', allInProgressProductionTasks.map((task) => task.id))
                    setProductionSubTab('inProgress')
                  }}
                  hidden={!hasSubFeature('production.inProgress', 'production')}
                >
                  В работе
                </button>
                <button
                  type="button"
                  className={productionSubTab === 'deferred' ? 'active' : ''}
                  onClick={() => setProductionSubTab('deferred')}
                  hidden={!hasSubFeature('production.deferred', 'production')}
                >
                  Отложенные
                </button>
                <button
                  type="button"
                  className={productionSubTab === 'completed' ? 'active' : ''}
                  onClick={() => setProductionSubTab('completed')}
                  hidden={!hasSubFeature('production.completed', 'production')}
                >
                  Выполненные
                </button>
              </div>

              {productionSubTab !== 'products' && (
                <div className="toolbar-row">
                  <input
                    className="toolbar-search"
                    placeholder="Поиск по товару, артикулу, статусу или исполнителю"
                    value={taskSearch}
                    onChange={(event) => setTaskSearch(event.target.value)}
                  />
                </div>
              )}

              {productionSubTab === 'products' && (
                <>
                  <div className="production-tools">
                    <form
                      className="search-form"
                      onSubmit={(event) => {
                        event.preventDefault()
                        loadProductionFiles(productionSearch)
                      }}
                    >
                      <input
                        placeholder="Поиск по любому полю товара или файла"
                        value={productionSearch}
                        onChange={(event) => setProductionSearch(event.target.value)}
                      />
                      <button type="submit">Найти</button>
                    </form>
                  </div>

                  <div className="section-title soft-title">
                    <h2>Товары на Ozon</h2>
                    <p>Ссылки, названия и артикулы</p>
                  </div>

                  <div className="data-table">
                <div className="table-row production-product-row table-head">
                  <span>Товар</span>
                  <span>Артикул</span>
                  <span>Файлы</span>
                  <span>Действия</span>
                </div>
                    {filteredProductionProducts.map((item) => {
                      const isSelected = selectedProductionProductId === item.productId
                      const itemFiles = productionFiles.filter(
                        (file) =>
                          file.offerId === item.offerId || file.ozonProductId === item.productId,
                      )

                      return (
                        <div className="product-row-group" key={item.productId}>
                          <div
                            className={`table-row production-product-row ${
                              isSelected ? 'selected-row' : ''
                            }`}
                          >
                            <span>
                              <strong>{item.name}</strong>
                              <small>{item.status}</small>
                            </span>
                            <span>{item.offerId}</span>
                            <span>
                              {itemFiles.map((file) => (
                                <button
                                  type="button"
                                  key={file.id}
                                  onClick={() => downloadProductionFile(file.id)}
                                >
                                  {file.fileName}
                                </button>
                              ))}
                            </span>
                            <span>
                              {item.productUrl ? (
                                <a href={item.productUrl} target="_blank" rel="noreferrer">
                                  Открыть
                                </a>
                              ) : (
                                '-'
                              )}
                            </span>
                            <span className="row-actions">
                              <button
                                type="button"
                                onClick={() =>
                                  setSelectedProductionProductId(isSelected ? null : item.productId)
                                }
                              >
                                {isSelected ? 'Скрыть' : 'Выбрать'}
                              </button>
                            </span>
                          </div>

                          {isSelected && (
                            <ProductDetail
                              product={item}
                              files={itemFiles}
                              userRole={user?.role}
                              onUpload={uploadProductionFile}
                              onFileChange={setUploadFile}
                              onDownload={downloadProductionFile}
                              onDelete={deleteProductionFile}
                            />
                          )}
                        </div>
                      )
                    })}
                  </div>
                </>
              )}

              {productionSubTab === 'tasks' && (
                <>
                  {isAdminLike && hasSubFeature('production.createTask', 'production') && (
                    <div className="supply-create-bar">
                      <button type="button" onClick={() => setShowCreateTaskModal(true)}>
                        Создать задачу
                      </button>
                    </div>
                  )}

                  {showCreateTaskModal && isAdminLike && (
                    <div className="modal-backdrop" role="presentation">
                      <div className="modal-card modal-card-wide" role="dialog" aria-modal="true">
                        <div className="modal-title-row">
                          <h3>Создать задачу</h3>
                          <button type="button" onClick={() => setShowCreateTaskModal(false)}>
                            Закрыть
                          </button>
                        </div>

                        <div className="task-form task-form-modal">
                          <ProductSearchInput
                            listId="task-products"
                            products={productionProducts}
                            supplierLinks={productSupplierLinks}
                            selectedProductId={selectedTaskProductId}
                            onProductIdChange={setSelectedTaskProductId}
                            placeholder="Начните писать название или артикул"
                          />
                          <input
                            type="number"
                            min="1"
                            placeholder="Нужно сделать, шт."
                            value={taskQuantity}
                            onChange={(event) => setTaskQuantity(event.target.value)}
                          />
                          <button type="button" onClick={addDraftTaskItem}>
                            Добавить
                          </button>
                        </div>

                        <div className="data-table modal-table">
                          <div className="table-row task-draft-row table-head">
                            <span>Товар</span>
                            <span>Артикул</span>
                            <span>Количество</span>
                            <span></span>
                          </div>
                          {draftTaskItems.map((item) => (
                            <div className="table-row task-draft-row" key={item.tempId}>
                              <span className="product-mini">
                                <ProductThumb imageUrl={item.imageUrl} name={item.productName} />
                                <span>
                                  <strong>{item.productName}</strong>
                                </span>
                              </span>
                              <span>{item.offerId}</span>
                              <span>{item.requiredQuantity}</span>
                              <span>
                                <button
                                  type="button"
                                  className="danger"
                                  onClick={() =>
                                    setDraftTaskItems((current) =>
                                      current.filter((task) => task.tempId !== item.tempId),
                                    )
                                  }
                                >
                                  Убрать
                                </button>
                              </span>
                            </div>
                          ))}
                          {draftTaskItems.length === 0 && (
                            <div className="empty-state">
                              <strong>Добавьте товары в задачу.</strong>
                            </div>
                          )}
                        </div>

                        <div className="supply-actions">
                          <button type="button" onClick={createProductionTasksFromDraft}>
                            Сохранить
                          </button>
                        </div>
                      </div>
                    </div>
                  )}

                  <ProductionTaskTable
                    tasks={newProductionTasks}
                    products={ozonProducts}
                    actualQuantities={actualQuantities}
                    setActualQuantities={setActualQuantities}
                    onStart={startProductionTask}
                    onDefer={deferProductionTask}
                    onComplete={completeProductionTask}
                  />
                </>
              )}

              {productionSubTab === 'inProgress' && (
                <ProductionTaskTable
                  tasks={inProgressProductionTasks}
                  products={ozonProducts}
                  actualQuantities={actualQuantities}
                  setActualQuantities={setActualQuantities}
                  onStart={startProductionTask}
                  onDefer={deferProductionTask}
                  onComplete={completeProductionTask}
                />
              )}

              {productionSubTab === 'deferred' && (
                <ProductionTaskTable
                  tasks={deferredProductionTasks}
                  products={ozonProducts}
                  actualQuantities={actualQuantities}
                  setActualQuantities={setActualQuantities}
                  onStart={startProductionTask}
                  onDefer={deferProductionTask}
                  onComplete={completeProductionTask}
                  deferred
                />
              )}

              {productionSubTab === 'completed' && (
                <ProductionTaskArchiveTable
                  tasks={completedProductionTasks}
                  products={ozonProducts}
                  onArchive={isAdminLike ? archiveProductionTask : undefined}
                  emptyText="Выполненных задач пока нет."
                />
              )}

              {productionSubTab === 'archive' && (
                <ProductionTaskArchiveTable
                  tasks={archivedProductionTasks}
                  products={ozonProducts}
                  onDelete={isAdminLike ? deleteProductionTask : undefined}
                  emptyText="В архиве задач пока нет."
                />
              )}
            </section>
          )}

          {activeTab === 'products' && (
            <section className="products">
              <div className="section-title">
                <h2>Товары</h2>
                <p>{isLoading ? 'Загрузка...' : 'Ответ получен от ASP.NET Core'}</p>
              </div>

              <div className="subtabs-placeholder">
                {isAdminLike && (
                  <button type="button" onClick={loadOzonProducts}>
                    Обновить товары Ozon
                  </button>
                )}
              </div>

              <div className="inner-tabs">
                <button
                  type="button"
                  className={productsSubTab === 'settings' ? 'active' : ''}
                  onClick={() => setProductsSubTab('settings')}
                >
                  Настройка товаров
                  {unsetProducts.length > 0 && <span className="tab-badge">{unsetProducts.length}</span>}
                </button>
                <button
                  type="button"
                  className={productsSubTab === 'production' ? 'active' : ''}
                  onClick={() => setProductsSubTab('production')}
                >
                  Производственные
                </button>
                <button
                  type="button"
                  className={productsSubTab === 'purchase' ? 'active' : ''}
                  onClick={() => setProductsSubTab('purchase')}
                >
                  Закупочные
                </button>
                <button
                  type="button"
                  className={productsSubTab === 'editor' ? 'active' : ''}
                  onClick={() => setProductsSubTab('editor')}
                >
                  Редактор товаров
                </button>
              </div>

              {ozonStatus && (
                <div className="ozon-status">
                  <strong>{ozonStatus}</strong>
                  {ozonProducts && (
                    <span>
                      Загружено: {ozonProducts.length}
                    </span>
                  )}
                </div>
              )}

              {productsSubTab === 'editor' && (
                <div className="product-editor-toolbar">
                  <label>
                    <span>Поиск по товарам</span>
                    <input
                      type="search"
                      value={productEditorSearch}
                      onChange={(event) => setProductEditorSearch(event.target.value)}
                      placeholder="Название, артикул, ID, тип товара"
                      autoComplete="off"
                      spellCheck={false}
                    />
                  </label>
                  <strong>Найдено: {productsBySubTab.length}</strong>
                </div>
              )}

              <div className="data-table">
                <div className="table-row ozon-product-row table-head">
                  <span>Товар</span>
                  <span>Артикул</span>
                  <span>Фото</span>
                  <span>Цена</span>
                  <span>Ссылка на Ozon</span>
                  <span>{productsSubTab === 'editor' ? 'Тип товара' : 'Поставщик'}</span>
                </div>
                {productsBySubTab.map((item) => (
                  <div
                    className={`table-row ozon-product-row ${productsSubTab === 'editor' ? 'product-editor-row' : ''}`}
                    key={item.productId}
                  >
                    <span>
                      <strong>{item.name}</strong>
                      <small>{item.productId}</small>
                    </span>
                    <span>{item.offerId}</span>
                    <span>
                      {item.imageUrl ? (
                        <img className="product-thumb" src={item.imageUrl} alt="" />
                      ) : (
                        '-'
                      )}
                    </span>
                    <span>{formatMoney(item.price, item.currencyCode)}</span>
                    <span>
                      {item.productUrl ? (
                        <a href={item.productUrl} target="_blank" rel="noreferrer">
                          Ссылка на Ozon
                        </a>
                      ) : (
                        item.status
                      )}
                    </span>
                    <span>
                      {productsSubTab === 'editor' ? (
                        <span className="product-editor-actions">
                          <span className={`product-type-badge product-type-${item.productType || 'Unset'}`}>
                            {formatProductType(item.productType)}
                          </span>
                          <button type="button" onClick={() => openProductTypeModal(item)}>
                            Сменить тип
                          </button>
                        </span>
                      ) : productsSubTab === 'settings' ? (
                        <span className="product-type-actions">
                          <button type="button" onClick={() => updateProductType(item, 'Production')}>
                            Производственный
                          </button>
                          <button type="button" onClick={() => updateProductType(item, 'Purchase')}>
                            Закупочный
                          </button>
                        </span>
                      ) : item.productType === 'Production' ? (
                        <span className="muted-text">Производственный товар</span>
                      ) : (productSupplierLinks[item.productId] ?? []).length > 0 ? (
                        <span className="supplier-link-actions">
                          <button type="button" onClick={() => openSupplierModal(item)}>
                            Поставщики: {(productSupplierLinks[item.productId] ?? []).length}
                          </button>
                          <button type="button" className="tiny-danger" onClick={() => deleteSupplierLinks(item)}>
                            Удалить все
                          </button>
                        </span>
                      ) : (
                        <span className="supplier-link-actions">
                          <button type="button" onClick={() => openSupplierModal(item)}>
                            Добавить поставщика
                          </button>
                        </span>
                      )}
                    </span>
                  </div>
                ))}
                {productsBySubTab.length === 0 && (
                  <div className="empty-state">
                    <strong>
                      {productsSubTab === 'settings'
                        ? 'Все товары уже настроены.'
                        : 'В этой подкладке пока нет товаров.'}
                    </strong>
                  </div>
                )}
              </div>
            </section>
          )}

          {activeTab === 'analytics' && (
            <section className="tab-panel">
              <div className="section-title">
                <h2>Аналитика</h2>
                <p>{analyticsStatus || 'Продажи и выручка из Ozon API'}</p>
              </div>
              <div className="inner-tabs">
                <button
                  type="button"
                  className={analyticsSubTab === 'summary' ? 'active' : ''}
                  onClick={() => setAnalyticsSubTab('summary')}
                  hidden={!hasSubFeature('analytics.summary', 'analytics')}
                >
                  Общая аналитика
                </button>
                <button
                  type="button"
                  className={analyticsSubTab === 'topProducts' ? 'active' : ''}
                  onClick={() => setAnalyticsSubTab('topProducts')}
                  hidden={!hasSubFeature('analytics.topProducts', 'analytics')}
                >
                  Топ товары
                </button>
              </div>
              <div className="subtabs-placeholder">
                <button type="button" onClick={loadAnalytics}>
                  Обновить аналитику
                </button>
              </div>
              {analyticsSubTab === 'summary' && (
                <>
                  <div className="analytics-grid">
                    <div>
                      <span>На счету Ozon</span>
                      <strong>
                        {analytics
                          ? formatPlainNumber(analytics.accountBalance?.amount ?? 0)
                          : '-'}
                      </strong>
                    </div>
                    <div>
                      <span>Товары Ozon</span>
                      <strong>{ozonProducts.length || '-'}</strong>
                    </div>
                    <div>
                      <span>Продано, шт.</span>
                      <strong>{analytics?.orderedUnitsTotal ?? '-'}</strong>
                    </div>
                    <div>
                      <span>Выручка</span>
                      <strong>{analytics ? formatMoney(analytics.revenueTotal, 'KZT') : '-'}</strong>
                    </div>
                    <div>
                      <span>Комиссия Ozon</span>
                      <strong>{analytics ? formatMoney(analytics.commissionTotal, 'KZT') : '-'}</strong>
                    </div>
                    <div>
                      <span>К выплате</span>
                      <strong>{analytics ? formatMoney(analytics.payoutTotal, 'KZT') : '-'}</strong>
                    </div>
                    <div>
                      <span>Логистика</span>
                      <strong>{analytics ? formatMoney(analytics.logisticsTotal, 'KZT') : '-'}</strong>
                    </div>
                    <div>
                      <span>Прочие услуги</span>
                      <strong>{analytics ? formatMoney(analytics.servicesTotal, 'KZT') : '-'}</strong>
                    </div>
                    <div>
                      <span>Собираются</span>
                      <strong>{analytics?.awaitingDeliverCount ?? '-'}</strong>
                    </div>
                    <div>
                      <span>Едут</span>
                      <strong>{analytics?.deliveringCount ?? '-'}</strong>
                    </div>
                    <div>
                      <span>Доставлены</span>
                      <strong>{analytics?.deliveredCount ?? '-'}</strong>
                    </div>
                  </div>
                  <div className="data-table">
                    <div className="table-row analytics-row table-head">
                      <span>Артикул</span>
                      <span>Товар</span>
                      <span>Статус</span>
                      <span>Шт.</span>
                      <span>Выручка</span>
                      <span>Комиссия</span>
                      <span>Логистика</span>
                      <span>К выплате</span>
                    </div>
                    {analytics?.rows.map((row) => (
                      <div className="table-row analytics-row" key={`${row.postingNumber}-${row.sku}`}>
                        <span>
                          <strong>{row.offerId}</strong>
                          <small>{row.sku}</small>
                        </span>
                        <span>{row.productName}</span>
                        <span>{translateStatus(row.status)}</span>
                        <span>{row.quantity}</span>
                        <span>{formatMoney(row.revenue, row.currencyCode || 'KZT')}</span>
                        <span>
                          {row.commissionPercent}% / {formatMoney(row.commissionAmount, row.currencyCode || 'KZT')}
                        </span>
                        <span>{formatMoney(row.logisticsAmount, row.currencyCode || 'KZT')}</span>
                        <span>{formatMoney(row.payout, row.currencyCode || 'KZT')}</span>
                      </div>
                    ))}
                  </div>
                </>
              )}
              {analyticsSubTab === 'topProducts' && (
                <>
                  <div className="ozon-status">
                    <strong>Все продажи без фильтра по статусу доставки</strong>
                    <span>Сортировка по количеству продаж</span>
                  </div>
                  <div className="data-table">
                    <div className="table-row top-products-row table-head">
                      <span>Место</span>
                      <span>Товар</span>
                      <span>Артикул</span>
                      <span>SKU</span>
                      <span>Продано</span>
                      <span>Сумма заказов</span>
                    </div>
                    {topAnalyticsProducts.map((row, index) => (
                      <div className="table-row top-products-row" key={row.key}>
                        <span>{index + 1}</span>
                        <span>
                          <strong>{row.productName}</strong>
                        </span>
                        <span>{row.offerId || '-'}</span>
                        <span>{row.sku || '-'}</span>
                        <span>{row.quantity}</span>
                        <span>{formatMoney(row.revenue, row.currencyCode)}</span>
                      </div>
                    ))}
                    {topAnalyticsProducts.length === 0 && (
                      <div className="empty-state">
                        <strong>Проданных товаров пока нет.</strong>
                      </div>
                    )}
                  </div>
                </>
              )}
            </section>
          )}

          {activeTab === 'pooling' && (
            <section className="tab-panel">
              <div className="section-title">
                <h2>Склад</h2>
                <p>{urgentStockStatus || priceStatus || stockStatus || 'Остатки товаров на складе Ozon'}</p>
              </div>
              <div className="subtabs-placeholder">
                <button type="button" onClick={loadOzonStocks}>
                  Обновить остатки Ozon
                </button>
                <button type="button" className="danger-action" onClick={checkUrgentStocks}>
                  Срочно
                </button>
                {urgentStockOnly && (
                  <button type="button" onClick={() => setUrgentStockOnly(false)}>
                    Все товары
                  </button>
                )}
                <input
                  className="toolbar-search"
                  placeholder="Поиск по товару, артикулу или цене"
                  value={stockSearch}
                  onChange={(event) => setStockSearch(event.target.value)}
                />
                <span className="sort-actions stock-sort-actions">
                  <button type="button" onClick={() => setStockSortKey('fbo')}>
                    FBO по возрастанию
                  </button>
                  <button type="button" onClick={() => setStockSortKey('fbs')}>
                    FBS по возрастанию
                  </button>
                </span>
              </div>
              <div className="data-table stock-table">
                <div className="table-row stock-row table-head">
                  <span>Товар</span>
                  <span>Артикул</span>
                  <span>FBO</span>
                  <span>FBS</span>
                  <span>Цена</span>
                  <span>Действие</span>
                </div>
                {sortedOzonStocks.map((item) => (
                  <StockRow
                    item={item}
                    key={item.productId}
                    urgentInfo={urgentStockItems.find((urgentItem) => urgentItem.productId === item.productId)}
                    priceValue={editingPrices[item.productId] ?? String(item.price)}
                    onPriceChange={(value) =>
                      setEditingPrices((current) => ({ ...current, [item.productId]: value }))
                    }
                    onSave={() => updateOzonPrice(item)}
                    canEditPrice={hasSubFeature('pooling.editPrices', 'pooling')}
                  />
                ))}
                {sortedOzonStocks.length === 0 && (
                  <div className="empty-state">
                    <strong>
                      {urgentStockOnly
                        ? 'Всех товаров хватает.'
                        : 'Остатков по выбранному фильтру пока нет.'}
                    </strong>
                  </div>
                )}
              </div>
            </section>
          )}

          {activeTab === 'supplies' && (
            <section className="tab-panel">
              <div className="section-title">
                <div>
                  <h2>Поставки</h2>
                  <p>{supplyStatus || 'Создание, статусы и аналитика поставок'}</p>
                </div>
                {isAdminLike && (
                  <button
                    type="button"
                    className="header-action"
                    onClick={() => setSupplySubTab('archive')}
                    hidden={!hasSubFeature('supplies.archive', 'supplies')}
                  >
                    Архив поставок
                  </button>
                )}
              </div>

              <div className="inner-tabs">
                <button
                  type="button"
                  className={supplySubTab === 'create' ? 'active' : ''}
                  onClick={() => setSupplySubTab('create')}
                  hidden={!hasSubFeature('supplies.create', 'supplies')}
                >
                  Создать поставку
                </button>
                {isAdminLike && (
                  <button
                    type="button"
                    className={supplySubTab === 'editor' ? 'active' : ''}
                    onClick={() => setSupplySubTab('editor')}
                    hidden={!hasSubFeature('supplies.editor', 'supplies')}
                  >
                    Редактор поставок
                  </button>
                )}
                <button
                  type="button"
                  className={supplySubTab === 'all' ? 'active' : ''}
                  onClick={() => setSupplySubTab('all')}
                  hidden={!hasSubFeature('supplies.all', 'supplies')}
                >
                  Все поставки
                </button>
                {isAdminLike && (
                  <button
                    type="button"
                    className={supplySubTab === 'analytics' ? 'active' : ''}
                    onClick={() => setSupplySubTab('analytics')}
                    hidden={!hasSubFeature('supplies.analytics', 'supplies')}
                  >
                  Аналитика по товару
                </button>
                )}
              </div>

              <div className="toolbar-row">
                <input
                  className="toolbar-search"
                  placeholder="Поиск по поставкам, товарам, артикулам"
                  value={supplySearch}
                  onChange={(event) => setSupplySearch(event.target.value)}
                />
                {supplySubTab === 'all' && (
                  <select
                    className="toolbar-select"
                    value={supplyStatusFilter}
                    onChange={(event) =>
                      setSupplyStatusFilter(event.target.value as 'all' | SupplyStatus)
                    }
                  >
                    <option value="all">Все статусы</option>
                    <option value="Created">Создано</option>
                    <option value="Sent">Отправлено</option>
                    <option value="Accepted">Принято</option>
                  </select>
                )}
              </div>

              {supplySubTab === 'create' && (
                <>
                  <div className="supply-create-bar">
                    <button type="button" onClick={() => setShowCreateSupplyModal(true)}>
                      Создать поставку
                    </button>
                    {isAdminLike && (
                      <>
                      <button type="button" onClick={downloadSupplyTemplate}>
                        Скачать Excel-шаблон
                      </button>
                      <input
                        type="file"
                        accept=".xlsx"
                        onChange={(event) => setSupplyImportFile(event.target.files?.[0] ?? null)}
                      />
                      <button type="button" onClick={uploadSupplyExcel}>
                        Загрузить Excel
                      </button>
                      </>
                    )}
                  </div>

                  {showCreateSupplyModal && (
                    <div className="modal-backdrop" role="presentation">
                      <div className="modal-card modal-card-wide" role="dialog" aria-modal="true">
                        <div className="modal-title-row">
                          <h3>Создать поставку</h3>
                          <button
                            type="button"
                            onClick={() => setShowCreateSupplyModal(false)}
                          >
                            Закрыть
                          </button>
                        </div>

                        <div className="supply-forms">
                          <div className="supply-form-block">
                            <span className="supply-form-title">
                              <strong>Товар из Ozon</strong>
                              <small>Для закупочных товаров из каталога</small>
                            </span>
                            <label className="supply-field supply-field-wide">
                              <span>Товар</span>
                              <ProductSearchInput
                                listId="supply-products"
                                products={purchaseProducts}
                                supplierLinks={productSupplierLinks}
                                selectedProductId={supplyProductId}
                                onProductIdChange={setSupplyProductId}
                                placeholder="Начните писать название или артикул"
                              />
                            </label>
                            <label className="supply-field">
                              <span>Количество</span>
                              <input
                                type="number"
                                min="1"
                                placeholder="Например: 20"
                                value={supplyQuantity}
                                onChange={(event) => setSupplyQuantity(event.target.value)}
                              />
                            </label>
                            <button type="button" onClick={addSupplyProduct}>
                              Добавить
                            </button>
                          </div>

                          <div className="supply-form-block">
                            <span className="supply-form-title">
                              <strong>Резервный товар</strong>
                              <small>Если товара еще нет в каталоге Ozon</small>
                            </span>
                            <label className="supply-field supply-field-wide">
                              <span>Название товара</span>
                              <input
                                placeholder="Например: упаковка для набора"
                                value={reserveProductName}
                                onChange={(event) => setReserveProductName(event.target.value)}
                              />
                            </label>
                            <label className="supply-field">
                              <span>Количество</span>
                              <input
                                type="number"
                                min="1"
                                placeholder="Например: 20"
                                value={reserveQuantity}
                                onChange={(event) => setReserveQuantity(event.target.value)}
                              />
                            </label>
                            <label className="supply-field">
                              <span>Поставщик</span>
                              <input
                                placeholder="Название поставщика"
                                value={reserveSupplierName}
                                onChange={(event) => setReserveSupplierName(event.target.value)}
                              />
                            </label>
                            <label className="supply-field supply-field-wide">
                              <span>Ссылка поставщика</span>
                              <input
                                placeholder="https://..."
                                value={reserveSupplierUrl}
                                onChange={(event) => setReserveSupplierUrl(event.target.value)}
                              />
                            </label>
                            <button type="button" onClick={addReserveSupplyProduct}>
                              Создать резервный товар
                            </button>
                          </div>
                        </div>

                        <div className="data-table modal-table">
                          <div className="table-row supply-item-row table-head">
                            <span>Товар</span>
                            <span>Артикул</span>
                            <span>План</span>
                            <span>Факт заказа</span>
                            <span>Источник</span>
                            <span>Поставщик</span>
                            <span></span>
                          </div>
                          {draftSupplyItems.map((item) => (
                            <div className="table-row supply-item-row" key={item.tempId}>
                              <span>
                                <strong>{item.productName}</strong>
                                <small>{item.isReserve ? 'Резервная строка' : 'Товар из Ozon'}</small>
                              </span>
                              <span>{item.offerId || '-'}</span>
                              <span>{item.quantity}</span>
                              <span>{item.actualOrderQuantity}</span>
                              <span>{item.isReserve ? 'Резервный' : 'Постоянный'}</span>
                              <span>
                                {item.supplierUrl ? (
                                  <a href={item.supplierUrl} target="_blank" rel="noreferrer">
                                    {item.supplierName || 'Открыть'}
                                  </a>
                                ) : (
                                  item.supplierName || '-'
                                )}
                              </span>
                              <span>
                                <button
                                  type="button"
                                  className="danger"
                                  onClick={() =>
                                    setDraftSupplyItems((current) =>
                                      current.filter((draft) => draft.tempId !== item.tempId),
                                    )
                                  }
                                >
                                  Убрать
                                </button>
                              </span>
                            </div>
                          ))}
                          {draftSupplyItems.length === 0 && (
                            <div className="empty-state">
                              <strong>Добавьте товары в поставку.</strong>
                            </div>
                          )}
                        </div>

                        <div className="supply-actions">
                          <button type="button" onClick={createSupply}>
                            Сохранить
                          </button>
                        </div>
                      </div>
                    </div>
                  )}

                  <SupplyTable
                    supplies={createdSupplies}
                    ozonProducts={ozonProducts}
                    productSupplierLinks={productSupplierLinks}
                    replaceProducts={replaceProducts}
                    setReplaceProducts={setReplaceProducts}
                    editingSupplyId={editingSupplyId}
                    editSupplyItems={editSupplyItems}
                    setEditSupplyItems={setEditSupplyItems}
                    editSupplyProductId={editSupplyProductId}
                    setEditSupplyProductId={setEditSupplyProductId}
                    editSupplyQuantity={editSupplyQuantity}
                    setEditSupplyQuantity={setEditSupplyQuantity}
                    editReserveProductName={editReserveProductName}
                    setEditReserveProductName={setEditReserveProductName}
                    editReserveQuantity={editReserveQuantity}
                    setEditReserveQuantity={setEditReserveQuantity}
                    editReserveSupplierName={editReserveSupplierName}
                    setEditReserveSupplierName={setEditReserveSupplierName}
                    editReserveSupplierUrl={editReserveSupplierUrl}
                    setEditReserveSupplierUrl={setEditReserveSupplierUrl}
                    onStartEdit={startEditSupply}
                    onCancelEdit={cancelEditSupply}
                    onAddEditProduct={addEditSupplyProduct}
                    onAddEditReserve={addEditReserveSupplyProduct}
                  onSaveEdit={saveSupplyEdit}
                  onDeleteSupply={deleteSupply}
                    onArchiveSupply={archiveSupply}
                    onStatusChange={updateSupplyStatus}
                    onDownloadOrder={downloadSupplyOrder}
                    supplyActualOrderEdits={supplyActualOrderEdits}
                    setSupplyActualOrderEdits={setSupplyActualOrderEdits}
                    onActualOrderChange={updateSupplyItemActualOrder}
                  userRole={user?.role}
                />
                </>
              )}

              {supplySubTab === 'editor' && (
                <SupplyTable
                  supplies={editableSupplies}
                  ozonProducts={ozonProducts}
                  productSupplierLinks={productSupplierLinks}
                  replaceProducts={replaceProducts}
                  setReplaceProducts={setReplaceProducts}
                  editingSupplyId={editingSupplyId}
                  editSupplyItems={editSupplyItems}
                  setEditSupplyItems={setEditSupplyItems}
                  editSupplyProductId={editSupplyProductId}
                  setEditSupplyProductId={setEditSupplyProductId}
                  editSupplyQuantity={editSupplyQuantity}
                  setEditSupplyQuantity={setEditSupplyQuantity}
                  editReserveProductName={editReserveProductName}
                  setEditReserveProductName={setEditReserveProductName}
                  editReserveQuantity={editReserveQuantity}
                  setEditReserveQuantity={setEditReserveQuantity}
                  editReserveSupplierName={editReserveSupplierName}
                  setEditReserveSupplierName={setEditReserveSupplierName}
                  editReserveSupplierUrl={editReserveSupplierUrl}
                  setEditReserveSupplierUrl={setEditReserveSupplierUrl}
                  onStartEdit={startEditSupply}
                  onCancelEdit={cancelEditSupply}
                  onAddEditProduct={addEditSupplyProduct}
                  onAddEditReserve={addEditReserveSupplyProduct}
                  onSaveEdit={saveSupplyEdit}
                  onDeleteSupply={deleteSupply}
                  onArchiveSupply={archiveSupply}
                  onStatusChange={updateSupplyStatus}
                  onDownloadOrder={downloadSupplyOrder}
                  supplyActualOrderEdits={supplyActualOrderEdits}
                  setSupplyActualOrderEdits={setSupplyActualOrderEdits}
                  onActualOrderChange={updateSupplyItemActualOrder}
                  userRole={user?.role}
                  hideItemsUntilEdit
                />
              )}

              {supplySubTab === 'all' && <AllSuppliesTable supplies={visibleAllSupplies} />}

              {supplySubTab === 'archive' && isAdminLike && (
                <SupplyTable
                  supplies={archivedSupplies}
                  ozonProducts={ozonProducts}
                  productSupplierLinks={productSupplierLinks}
                  replaceProducts={replaceProducts}
                  setReplaceProducts={setReplaceProducts}
                  editingSupplyId={editingSupplyId}
                  editSupplyItems={editSupplyItems}
                  setEditSupplyItems={setEditSupplyItems}
                  editSupplyProductId={editSupplyProductId}
                  setEditSupplyProductId={setEditSupplyProductId}
                  editSupplyQuantity={editSupplyQuantity}
                  setEditSupplyQuantity={setEditSupplyQuantity}
                  editReserveProductName={editReserveProductName}
                  setEditReserveProductName={setEditReserveProductName}
                  editReserveQuantity={editReserveQuantity}
                  setEditReserveQuantity={setEditReserveQuantity}
                  editReserveSupplierName={editReserveSupplierName}
                  setEditReserveSupplierName={setEditReserveSupplierName}
                  editReserveSupplierUrl={editReserveSupplierUrl}
                  setEditReserveSupplierUrl={setEditReserveSupplierUrl}
                  onStartEdit={startEditSupply}
                  onCancelEdit={cancelEditSupply}
                  onAddEditProduct={addEditSupplyProduct}
                  onAddEditReserve={addEditReserveSupplyProduct}
                  onSaveEdit={saveSupplyEdit}
                  onDeleteSupply={deleteSupply}
                  onArchiveSupply={archiveSupply}
                  onStatusChange={updateSupplyStatus}
                  onDownloadOrder={downloadSupplyOrder}
                  supplyActualOrderEdits={supplyActualOrderEdits}
                  setSupplyActualOrderEdits={setSupplyActualOrderEdits}
                  onActualOrderChange={updateSupplyItemActualOrder}
                  userRole={user?.role}
                  archiveMode
                  hideItemsUntilEdit
                />
              )}

              {supplySubTab === 'analytics' && (
                <>
                  <div className="supply-filter">
                    <select
                      value={analyticsProductKey}
                      onChange={(event) => setAnalyticsProductKey(event.target.value)}
                    >
                      <option value="">Все товары</option>
                      {Array.from(
                        new Map(
                          supplyAnalytics.map((item) => [
                            item.isReserve
                              ? `reserve:${item.productName}`
                              : `product:${item.ozonProductId}`,
                            item,
                          ]),
                        ).values(),
                      ).map((item) => (
                        <option
                          value={
                            item.isReserve
                              ? `reserve:${item.productName}`
                              : `product:${item.ozonProductId}`
                          }
                          key={`${item.supplyId}-${item.id}`}
                        >
                          {item.productName}
                        </option>
                      ))}
                    </select>
                    <button type="button" onClick={loadSupplyAnalytics}>
                      Обновить
                    </button>
                    <button type="button" onClick={exportSupplyAnalytics}>
                      Скачать CSV
                    </button>
                  </div>

                  <SupplyAnalyticsTable rows={filteredSupplyAnalytics} />
                </>
              )}
            </section>
          )}

          {activeTab === 'chats' && (
            <section className="tab-panel chat-panel">
              <div className="section-title">
                <div>
                  <h2>Чаты</h2>
                  <p>{chatStatus || 'Обмен сообщениями между пользователями'}</p>
                </div>
                <button type="button" className="header-action" onClick={loadChatUsers}>
                  Обновить
                </button>
              </div>

              <div className="chat-layout">
                <aside className="chat-users">
                  {chatUsers.map((item) => (
                    <button
                      type="button"
                      className={selectedChatUserId === item.id ? 'active' : ''}
                      onClick={() => setSelectedChatUserId(item.id)}
                      key={item.id}
                    >
                      <span className="chat-avatar avatar-presence">
                        {item.avatarUrl ? <img src={item.avatarUrl} alt="" /> : <span>Фото</span>}
                        <span
                          className={`presence-dot ${item.isOnline ? 'is-online' : 'is-offline'}`}
                          aria-label={item.isOnline ? 'В сети' : 'Не в сети'}
                        />
                      </span>
                      <span>
                        <strong>{item.displayName || item.userName}</strong>
                        <small>{item.position || 'Должность не указана'}</small>
                      </span>
                      {(item.unreadCount ?? 0) > 0 && (
                        <span className="tab-badge">{item.unreadCount}</span>
                      )}
                    </button>
                  ))}
                  {chatUsers.length === 0 && (
                    <div className="empty-state">
                      <strong>Пока нет других пользователей для переписки.</strong>
                    </div>
                  )}
                </aside>

                <section className="chat-window">
                  {selectedChatUser ? (
                    <>
                      <div className="chat-window-head">
                        <span className="chat-avatar avatar-presence">
                          {selectedChatUser.avatarUrl ? (
                            <img src={selectedChatUser.avatarUrl} alt="" />
                          ) : (
                            <span>Фото</span>
                          )}
                          <span
                            className={`presence-dot ${selectedChatUser.isOnline ? 'is-online' : 'is-offline'}`}
                            aria-label={selectedChatUser.isOnline ? 'В сети' : 'Не в сети'}
                          />
                        </span>
                        <span>
                          <strong>{selectedChatUser.displayName || selectedChatUser.userName}</strong>
                          <small>{selectedChatUser.position || 'Должность не указана'}</small>
                        </span>
                      </div>

                      <div className="chat-messages">
                        {chatMessages.map((message) => (
                          <div
                            className={`chat-message ${message.isOwn ? 'own' : 'incoming'}`}
                            key={message.id}
                          >
                            {message.text && <p>{message.text}</p>}
                            {message.hasAttachment && (
                              <button
                                type="button"
                                className="chat-attachment"
                                onClick={() => downloadChatAttachment(message)}
                              >
                                <span>{isImageAttachment(message) ? 'Скриншот' : 'Файл'}</span>
                                <strong>{message.attachmentFileName}</strong>
                              </button>
                            )}
                            <span>
                              {formatDateTime(message.createdAt)}
                              {(message.isOwn || isAdminLike) && (
                                <button type="button" onClick={() => deleteChatMessage(message.id)}>
                                  Удалить
                                </button>
                              )}
                            </span>
                          </div>
                        ))}
                        {chatMessages.length === 0 && (
                          <div className="empty-state">
                            <strong>Сообщений пока нет.</strong>
                          </div>
                        )}
                        <div ref={chatMessagesEndRef} />
                      </div>

                      <form className="chat-form" onSubmit={sendChatMessage}>
                        <div className="chat-compose">
                          <textarea
                            placeholder="Напишите сообщение"
                            value={chatText}
                            onChange={(event) => setChatText(event.target.value)}
                            onKeyDown={(event) => {
                              if (event.key === 'Enter' && !event.shiftKey) {
                                event.preventDefault()
                                event.currentTarget.form?.requestSubmit()
                              }
                            }}
                            rows={3}
                          />
                          {chatFile && (
                            <div className="chat-file-preview">
                              <span>{chatFile.name}</span>
                              <button type="button" onClick={() => setChatFile(null)}>
                                Убрать
                              </button>
                            </div>
                          )}
                        </div>
                        <label className="chat-file-button">
                          Прикрепить
                          <input
                            type="file"
                            accept="image/*,.pdf,.doc,.docx,.xls,.xlsx,.txt,.zip,.rar"
                            onChange={(event) => setChatFile(event.target.files?.[0] ?? null)}
                          />
                        </label>
                        <button type="submit">Отправить</button>
                      </form>
                    </>
                  ) : (
                    <div className="empty-state">
                      <strong>Выберите пользователя слева.</strong>
                    </div>
                  )}
                </section>
              </div>
            </section>
          )}

          {activeTab === 'integration' && isAdminLike && (
            <section className="admin-panel">
              <div className="section-title">
                <div>
                  <h2>Интеграция</h2>
                  <p>{ozonIntegrationStatus || 'Ozon ID и ключ хранятся зашифрованными для вашей компании'}</p>
                </div>
                <button type="button" className="header-action" onClick={loadOzonIntegrationStatus}>
                  Проверить Ozon
                </button>
              </div>

              <div className="settings-grid">
                <article className="settings-card">
                  <span>Компания</span>
                  <strong>{user.companyName || 'Компания'}</strong>
                  <small>ID компании: {user.companyId}</small>
                  <small>Сотрудники и интеграции привязаны к этой компании.</small>
                </article>

                <article className="settings-card">
                  <span>Ozon API</span>
                  <strong>
                    {ozonIntegration
                      ? ozonIntegration.success
                        ? 'Подключено'
                        : ozonIntegration.configured
                          ? 'Ключи сохранены'
                          : 'Не настроено'
                      : 'Не проверено'}
                  </strong>
                  <small>
                    {ozonIntegration
                      ? `Client ID: ${ozonIntegration.clientIdMasked || '-'} | API Key: ${
                          ozonIntegration.apiKeyMasked || '-'
                        }`
                      : 'Введите Ozon ID и API-ключ.'}
                  </small>
                </article>

                <article className="settings-card">
                  <span>Telegram</span>
                  <strong>
                    {telegramIntegration
                      ? telegramIntegration.linked
                        ? 'Подключено'
                        : telegramIntegration.botConfigured
                          ? 'Ожидает привязки'
                          : 'Бот не настроен'
                      : 'Не проверено'}
                  </strong>
                  <small>
                    {telegramIntegration?.linked
                      ? `Чат: ${telegramIntegration.chatTitle || 'Telegram'}`
                      : telegramIntegration?.botConfigured
                        ? `Код: ${telegramIntegration.linkCode}`
                        : 'Добавьте TELEGRAM_BOT_TOKEN и TELEGRAM_BOT_USERNAME в env.'}
                  </small>
                </article>
              </div>

              <form className="user-form" onSubmit={saveOzonIntegration}>
                <label>
                  <span>Ozon ID</span>
                  <input
                    value={ozonClientId}
                    onChange={(event) => setOzonClientId(event.target.value)}
                    placeholder="Client-Id из кабинета Ozon"
                    required
                  />
                </label>
                <label>
                  <span>API-ключ Ozon</span>
                  <input
                    value={ozonApiKey}
                    onChange={(event) => setOzonApiKey(event.target.value)}
                    placeholder="Api-Key из кабинета Ozon"
                    type="password"
                    autoComplete="new-password"
                    required
                  />
                </label>
                <button type="submit">Сохранить Ozon</button>
              </form>

              <section className="integration-card">
                <div>
                  <p className="eyebrow">Telegram-бот</p>
                  <h3>Уведомления в Telegram</h3>
                  <p>
                    Привяжите чат компании, чтобы получать критические уведомления по остаткам, поставкам,
                    производству и подписке.
                  </p>
                  {telegramIntegrationStatus && <p className="status-line">{telegramIntegrationStatus}</p>}
                  {telegramIntegration?.startUrl && !telegramIntegration.linked && (
                    <p className="trial-note">Откройте бота и нажмите Start. Код привязки уже добавлен в ссылку.</p>
                  )}
                </div>
                <div className="integration-actions">
                  {telegramIntegration?.startUrl && !telegramIntegration.linked && (
                    <a className="button-link" href={telegramIntegration.startUrl} target="_blank" rel="noreferrer">
                      Открыть Telegram-бота
                    </a>
                  )}
                  <button type="button" onClick={loadTelegramIntegrationStatus}>
                    Проверить Telegram
                  </button>
                  <button type="button" onClick={regenerateTelegramLink}>
                    Новый код
                  </button>
                  <button type="button" onClick={sendTelegramTest} disabled={!telegramIntegration?.linked}>
                    Отправить тест
                  </button>
                  <button type="button" className="danger" onClick={unlinkTelegram} disabled={!telegramIntegration?.linked}>
                    Отвязать
                  </button>
                </div>
              </section>
            </section>
          )}

          {activeTab === 'platform' && isSuperAdmin && (
            <section className="admin-panel platform-admin-panel">
              <div className="section-title">
                <h2>Администрирование</h2>
                <p>Управление компаниями, пользователями и подписками Fulvero</p>
              </div>

              <div className="inner-tabs">
                <button type="button" className={platformSubTab === 'dashboard' ? 'active' : ''} onClick={() => setPlatformSubTab('dashboard')}>
                  Dashboard
                </button>
                <button type="button" className={platformSubTab === 'companies' ? 'active' : ''} onClick={() => setPlatformSubTab('companies')}>
                  Компании
                </button>
                <button type="button" className={platformSubTab === 'users' ? 'active' : ''} onClick={() => setPlatformSubTab('users')}>
                  Пользователи
                </button>
              </div>

              {platformStatus && <p className="status-line">{platformStatus}</p>}

              {platformSubTab === 'dashboard' && (
                <div className="dashboard-grid platform-dashboard-grid">
                  <article className="metric-card">
                    <strong>{platformDashboard?.totalCompanies ?? 0}</strong>
                    <span>всего компаний</span>
                  </article>
                  <article className="metric-card">
                    <strong>{platformDashboard?.trialCompanies ?? 0}</strong>
                    <span>на триале</span>
                  </article>
                  <article className="metric-card">
                    <strong>{platformDashboard?.activeSubscriptions ?? 0}</strong>
                    <span>активных подписок</span>
                  </article>
                  <article className="metric-card">
                    <strong>{platformDashboard?.pastDueSubscriptions ?? 0}</strong>
                    <span>просроченных</span>
                  </article>
                  <article className="metric-card">
                    <strong>{platformDashboard?.totalUsers ?? 0}</strong>
                    <span>пользователей</span>
                  </article>
                  <article className="metric-card">
                    <strong>{platformDashboard?.ozonConnections ?? 0}</strong>
                    <span>Ozon подключений</span>
                  </article>
                  <article className="metric-card">
                    <strong>{platformDashboard?.telegramConnections ?? 0}</strong>
                    <span>Telegram подключений</span>
                  </article>
                </div>
              )}

              {platformSubTab === 'companies' && (
                <div className="table-wrap platform-table-wrap">
                  <table>
                    <thead>
                      <tr>
                        <th>Компания</th>
                        <th>Регистрация</th>
                        <th>Подписка</th>
                        <th>Окончание</th>
                        <th>Пользователи</th>
                        <th>Ozon</th>
                        <th>Telegram</th>
                        <th>Действия</th>
                      </tr>
                    </thead>
                    <tbody>
                      {platformCompanies.map((company) => (
                        <tr key={company.id}>
                          <td>
                            <strong>{company.name}</strong>
                            {company.isSystemCompany && <small>Системная</small>}
                          </td>
                          <td>{formatDateTime(company.createdAt)}</td>
                          <td>{company.subscriptionStatus}</td>
                          <td>{company.subscriptionPaidUntil ? formatDateTime(company.subscriptionPaidUntil) : '-'}</td>
                          <td>{company.userCount}</td>
                          <td>{company.ozonConnected ? 'Да' : 'Нет'}</td>
                          <td>{company.telegramConnected ? 'Да' : 'Нет'}</td>
                          <td>
                            <div className="platform-actions">
                              <button type="button" onClick={() => extendCompanyTrial(company)} disabled={company.isSystemCompany}>
                                Триал
                              </button>
                              <button type="button" onClick={() => changeCompanySubscription(company)}>
                                Подписка
                              </button>
                              <button type="button" onClick={() => blockCompany(company)} disabled={company.isSystemCompany || company.subscriptionStatus === 'Blocked'}>
                                Блок
                              </button>
                              <button type="button" onClick={() => unblockCompany(company)} disabled={company.isSystemCompany || company.subscriptionStatus !== 'Blocked'}>
                                Разблок
                              </button>
                              <button type="button" onClick={() => impersonateCompany(company)} disabled={company.isSystemCompany}>
                                Войти как компания
                              </button>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              {platformSubTab === 'users' && (
                <div className="table-wrap platform-table-wrap">
                  <table>
                    <thead>
                      <tr>
                        <th>Компания</th>
                        <th>Логин</th>
                        <th>Email</th>
                        <th>Роль</th>
                        <th>Регистрация</th>
                      </tr>
                    </thead>
                    <tbody>
                      {platformUsers.map((item) => (
                        <tr key={item.id}>
                          <td>{item.companyName}</td>
                          <td>{item.userName}</td>
                          <td>{item.email || '-'}</td>
                          <td>{item.role}</td>
                          <td>{formatDateTime(item.createdAt)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>
          )}

          {activeTab === 'users' && isAdminLike && (
            <section className="admin-panel">
              <div className="section-title">
                <h2>Пользователи</h2>
                <p>Добавляет только админ</p>
              </div>

              <form className="user-form" onSubmit={createUser} autoComplete="off">
                <label>
                  <span>Логин</span>
                  <input
                    name="manual-user-login"
                    autoComplete="off"
                    placeholder="Логин"
                    value={newUser.userName}
                    onChange={(event) => setNewUser({ ...newUser, userName: event.target.value })}
                    required
                  />
                </label>
                <label>
                  <span>Имя</span>
                  <input
                    name="manual-user-display"
                    autoComplete="off"
                    placeholder="Имя"
                    value={newUser.displayName}
                    onChange={(event) => setNewUser({ ...newUser, displayName: event.target.value })}
                    required
                  />
                </label>
                <label>
                  <span>Должность</span>
                  <input
                    name="manual-user-position"
                    autoComplete="off"
                    placeholder="Должность"
                    value={newUser.position}
                    onChange={(event) => setNewUser({ ...newUser, position: event.target.value })}
                  />
                </label>
                <label>
                  <span>Пароль</span>
                  <input
                    name="manual-user-secret"
                    autoComplete="new-password"
                    placeholder="Пароль"
                    type="password"
                    value={newUser.password}
                    onChange={(event) => setNewUser({ ...newUser, password: event.target.value })}
                    required
                  />
                </label>
                <label>
                  <span>Роль</span>
                  <select
                    value={newUser.role}
                    onChange={(event) => setNewUser({ ...newUser, role: event.target.value })}
                  >
                    <option value="User">User</option>
                    <option value="Admin">Admin</option>
                  </select>
                </label>
                <button type="submit">Добавить</button>
                <details className="user-settings-panel user-form-features">
                  <summary>Права доступа</summary>
                  <div className="feature-checks">
                    {featureGroups.map((group) => (
                      <fieldset key={group.title}>
                        <legend>{group.title}</legend>
                        {group.items.map((feature) => (
                          <label key={feature.id}>
                            <input
                              type="checkbox"
                              checked={newUser.role === 'Admin' || newUser.allowedFeatures.includes(feature.id)}
                              disabled={newUser.role === 'Admin'}
                              onChange={(event) =>
                                setNewUser((current) => ({
                                  ...current,
                                  allowedFeatures: event.target.checked
                                    ? [...current.allowedFeatures, feature.id]
                                    : current.allowedFeatures.filter((item) => item !== feature.id),
                                }))
                              }
                            />
                            {feature.label}
                          </label>
                        ))}
                      </fieldset>
                    ))}
                  </div>
                </details>
              </form>

              <div className="user-search-bar">
                <div
                  className="user-search-input"
                  contentEditable
                  role="textbox"
                  aria-label="Поиск по пользователям"
                  data-placeholder="Поиск по пользователям"
                  spellCheck={false}
                  onInput={(event) => setUserSearch(event.currentTarget.textContent ?? '')}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') {
                      event.preventDefault()
                    }
                  }}
                  onPaste={(event) => {
                    event.preventDefault()
                    const text = event.clipboardData.getData('text/plain')
                    document.execCommand('insertText', false, text)
                  }}
                />
                <span>Найдено: {filteredUsers.length}</span>
              </div>

              <ul className="users-list">
                {filteredUsers.map((item) => {
                  const edit = userSettingsEdits[item.id] ?? item
                  return (
                  <li key={item.id}>
                    <span>
                      <span className="user-card-head">
                        <span className="chat-avatar avatar-presence">
                          {item.avatarUrl ? <img src={item.avatarUrl} alt="" /> : <span>Фото</span>}
                          <span
                            className={`presence-dot ${item.isOnline ? 'is-online' : 'is-offline'}`}
                            aria-label={item.isOnline ? 'В сети' : 'Не в сети'}
                          />
                        </span>
                        <span>
                          <strong>{item.displayName || item.userName}</strong>
                          <small>ID сотрудника: {item.id}</small>
                          <small>Логин: {item.userName}</small>
                          <small>{item.position || 'Должность не указана'}</small>
                        </span>
                      </span>
                    </span>
                    <b>{item.role}</b>
                    <span className="online-status">
                      {item.isOnline ? (
                        <small>В сети</small>
                      ) : item.lastSeenAt ? (
                        <small>Был: {formatDateTime(item.lastSeenAt)}</small>
                      ) : (
                        <small>-</small>
                      )}
                    </span>
                    <input
                      placeholder="Новый пароль"
                      type="password"
                      value={passwordEdits[item.id] ?? ''}
                      onChange={(event) =>
                        setPasswordEdits((current) => ({
                          ...current,
                          [item.id]: event.target.value,
                        }))
                      }
                    />
                    <button type="button" onClick={() => changeUserPassword(item.id)}>
                      Сменить пароль
                    </button>
                    <button type="button" className="danger" onClick={() => deleteUser(item.id)}>
                      Удалить
                    </button>
                    <details className="user-settings-panel">
                      <summary>Настройки пользователя · ID: {item.id}</summary>
                      <div className="user-settings-grid">
                        <label>
                          <span>Имя</span>
                          <input
                            placeholder="Имя"
                            value={edit.displayName}
                            onChange={(event) =>
                              setUserSettingsEdits((current) => ({
                                ...current,
                                [item.id]: { ...edit, displayName: event.target.value },
                              }))
                            }
                          />
                        </label>
                        <label>
                          <span>Должность</span>
                          <input
                            placeholder="Должность"
                            value={edit.position}
                            onChange={(event) =>
                              setUserSettingsEdits((current) => ({
                                ...current,
                                [item.id]: { ...edit, position: event.target.value },
                              }))
                            }
                          />
                        </label>
                        <label>
                          <span>Роль</span>
                          <select
                            value={edit.role}
                            onChange={(event) =>
                              setUserSettingsEdits((current) => ({
                                ...current,
                                [item.id]: { ...edit, role: event.target.value },
                              }))
                            }
                          >
                            <option value="User">User</option>
                            <option value="Admin">Admin</option>
                          </select>
                        </label>
                        <div className="feature-checks">
                          {featureGroups.map((group) => (
                            <fieldset key={group.title}>
                              <legend>{group.title}</legend>
                              {group.items.map((feature) => (
                                <label key={feature.id}>
                                  <input
                                    type="checkbox"
                                    checked={edit.role === 'Admin' || edit.allowedFeatures.includes(feature.id)}
                                    disabled={edit.role === 'Admin'}
                                    onChange={(event) =>
                                      setUserSettingsEdits((current) => ({
                                        ...current,
                                        [item.id]: {
                                          ...edit,
                                          allowedFeatures: event.target.checked
                                            ? [...edit.allowedFeatures, feature.id]
                                            : edit.allowedFeatures.filter((value) => value !== feature.id),
                                        },
                                      }))
                                    }
                                  />
                                  {feature.label}
                                </label>
                              ))}
                            </fieldset>
                          ))}
                        </div>
                        <button type="button" onClick={() => saveUserSettings(item.id)}>
                          Сохранить настройки
                        </button>
                      </div>
                    </details>
                  </li>
                  )
                })}
                {filteredUsers.length === 0 && (
                  <li className="empty-user-search">
                    <strong>Пользователи не найдены.</strong>
                  </li>
                )}
              </ul>
            </section>
          )}

          {activeTab === 'settings' && isAdminLike && (
            <section className="admin-panel">
              <div className="section-title">
                <div>
                  <h2>Настройки</h2>
                  <p>{auditStatus || 'Настройки компании и журнал действий'}</p>
                </div>
                <span className="section-actions">
                  <button type="button" className="header-action" onClick={() => loadAuditLogs()}>
                    Обновить журнал
                  </button>
                  <button type="button" className="header-action" onClick={exportAuditLogs}>
                    Скачать CSV
                  </button>
                </span>
              </div>

              <div className="settings-grid">
                <div>
                  <span>Тариф</span>
                  <strong>{user.hasActiveSubscription ? 'Активен' : 'Нужна оплата'}</strong>
                  <small>{getTrialRemainingText(user.trialEndsAt)}</small>
                  <small>{getSubscriptionRemainingText(user.subscriptionPaidUntil)}</small>
                  {isAdminLike ? (
                    <button type="button" className="settings-card-action" onClick={startSubscriptionCheckout}>
                      Продлить тариф
                    </button>
                  ) : (
                    <small>Продлить тариф может только администратор компании.</small>
                  )}
                  {billingStatus && <small>{billingStatus}</small>}
                </div>
                <div>
                  <span>Сервер</span>
                  <strong>{systemHealth ? 'Работает' : 'Проверка...'}</strong>
                  <small>{systemHealth ? 'Сервер приложения доступен.' : 'Статус загружается'}</small>
                </div>
                <div>
                  <span>Ozon API</span>
                  <strong>
                    {ozonIntegration
                      ? ozonIntegration.success
                        ? 'Подключен'
                        : 'Нужна проверка'
                      : 'Проверка...'}
                  </strong>
                  <small>{ozonIntegrationStatus || 'Ключи не показываются полностью.'}</small>
                  {ozonIntegration && (
                    <small>
                      ClientId: {ozonIntegration.clientIdMasked} | ApiKey:{' '}
                      {ozonIntegration.apiKeyMasked}
                    </small>
                  )}
                  <button type="button" className="settings-card-action" onClick={loadOzonIntegrationStatus}>
                    Проверить Ozon
                  </button>
                </div>
              </div>

              <details className="audit-panel">
                <summary className="backup-panel-head">
                  <div>
                    <h3>Журнал действий</h3>
                    <p>{auditStatus || 'Последние действия пользователей и системы'}</p>
                  </div>
                </summary>
                <form
                  className="audit-filter"
                  onSubmit={(event) => {
                    event.preventDefault()
                    loadAuditLogs(auditSearch)
                  }}
                >
                  <input
                    placeholder="Поиск по журналу"
                    value={auditSearch}
                    onChange={(event) => setAuditSearch(event.target.value)}
                  />
                  <button type="submit">Найти</button>
                </form>

                <div className="data-table audit-table">
                  <div className="table-row audit-row table-head">
                    <span>Дата</span>
                    <span>Пользователь</span>
                    <span>Действие</span>
                    <span>Объект</span>
                    <span>Детали</span>
                  </div>
                  {auditLogs.map((log) => (
                    <div className="table-row audit-row" key={log.id}>
                      <span>{formatDateTime(log.createdAt)}</span>
                      <span>
                        <strong>{log.displayName || log.userName || '-'}</strong>
                        <small>{log.userName}</small>
                      </span>
                      <span>{log.action}</span>
                      <span>
                        <strong>{log.entityType}</strong>
                        <small>{log.entityId}</small>
                      </span>
                      <span>{log.details}</span>
                    </div>
                  ))}
                  {auditLogs.length === 0 && (
                    <div className="empty-state">
                      <strong>В журнале пока нет записей.</strong>
                    </div>
                  )}
                </div>
              </details>
            </section>
          )}
        </section>
      </div>
    </main>
  )
}

function getTaskNotificationStorageKey(userId: string, kind: 'new' | 'in-progress') {
  return `Fulvero:${userId}:seen-production-${kind}-tasks`
}

function readStringListFromStorage(key: string) {
  try {
    const value = localStorage.getItem(key)
    const parsed = value ? JSON.parse(value) : []
    return Array.isArray(parsed) ? parsed.filter((item) => typeof item === 'string') : []
  } catch {
    return []
  }
}

function isImageAttachment(message: ChatMessage) {
  return message.attachmentContentType.toLowerCase().startsWith('image/')
}

function ProductSearchInput({
  listId: _listId,
  products,
  selectedProductId,
  onProductIdChange,
  placeholder,
  supplierLinks = {},
  required = false,
}: {
  listId: string
  products: OzonProduct[]
  selectedProductId: string
  onProductIdChange: (productId: string) => void
  placeholder: string
  supplierLinks?: Record<number, ProductSupplierLink[]>
  required?: boolean
}) {
  const selectedProduct = products.find((product) => String(product.productId) === selectedProductId)
  const selectedLabel = selectedProduct ? formatProductSelectedLabel(selectedProduct) : ''
  const [query, setQuery] = useState(selectedLabel)
  const [isOpen, setIsOpen] = useState(false)
  const normalizedQuery = query.trim().toLowerCase()
  const filteredProducts = normalizedQuery
    ? products
        .filter((product) =>
          [
            product.name,
            product.offerId,
            product.sku,
            product.productId,
            product.status,
          ]
            .filter((value) => value !== undefined && value !== null)
            .some((value) => String(value).toLowerCase().includes(normalizedQuery)),
        )
        .slice(0, 80)
    : products.slice(0, 80)

  useEffect(() => {
    setQuery(selectedLabel)
  }, [selectedLabel])

  function handleChange(value: string) {
    setQuery(value)
    setIsOpen(true)

    const selected = products.find((product) => {
      const productId = String(product.productId)
      return productId === value || formatProductOption(product) === value || formatProductSelectedLabel(product) === value
    })

    onProductIdChange(selected ? String(selected.productId) : '')
  }

  function selectProduct(product: OzonProduct) {
    onProductIdChange(String(product.productId))
    setQuery(formatProductSelectedLabel(product))
    setIsOpen(false)
  }

  return (
    <div className="product-search-wrap">
      <div className="product-search-control">
        <input
          className="product-search-input"
          placeholder={placeholder}
          value={query}
          onChange={(event) => handleChange(event.target.value)}
          onFocus={() => setIsOpen(true)}
          onBlur={() => window.setTimeout(() => setIsOpen(false), 120)}
          required={required}
        />
        {selectedProduct && <ProductThumb imageUrl={selectedProduct.imageUrl} name={selectedProduct.name} />}
        {isOpen && filteredProducts.length > 0 && (
          <div className="product-search-menu" id={_listId}>
            {filteredProducts.map((product) => (
              <button
                type="button"
                key={product.productId}
                onMouseDown={(event) => event.preventDefault()}
                onClick={() => selectProduct(product)}
              >
                <ProductThumb imageUrl={product.imageUrl} name={product.name} />
                <span>
                  <strong>{product.offerId}</strong>
                  <small>{product.name}</small>
                </span>
              </button>
            ))}
          </div>
        )}
      </div>
      {selectedProduct && (
        <div className="selected-product-card">
          <ProductThumb imageUrl={selectedProduct.imageUrl} name={selectedProduct.name} />
          <span>
            <strong>{selectedProduct.name}</strong>
            <small>
              {selectedProduct.offerId}
              {selectedProduct.sku ? ` | SKU ${selectedProduct.sku}` : ''}
            </small>
          </span>
          {(supplierLinks[selectedProduct.productId] ?? []).length > 0 && (
            <span className="selected-supplier-links">
              {(supplierLinks[selectedProduct.productId] ?? []).map((link) => (
                <a href={link.supplierUrl} target="_blank" rel="noreferrer" key={link.id ?? link.supplierUrl}>
                  {link.supplierName || 'Поставщик'}
                </a>
              ))}
            </span>
          )}
        </div>
      )}
    </div>
  )
}

function formatProductOption(product: OzonProduct) {
  const sku = product.sku ? ` | SKU ${product.sku}` : ''
  return `${product.offerId} | ${product.name}${sku} | ID ${product.productId}`
}

function formatProductSelectedLabel(product: OzonProduct) {
  const name = product.name.length > 64 ? `${product.name.slice(0, 64)}...` : product.name
  return `${product.offerId} | ${name}`
}

function ProductThumb({ imageUrl, name }: { imageUrl?: string; name: string }) {
  return (
    <span className="product-thumb">
      {imageUrl ? <img src={imageUrl} alt={name} loading="lazy" /> : <span>Фото</span>}
    </span>
  )
}

function ProductDetail({
  product,
  files,
  userRole,
  onUpload,
  onFileChange,
  onDownload,
  onDelete,
}: {
  product: OzonProduct
  files: ProductionFile[]
  userRole?: string
  onUpload: (event: FormEvent<HTMLFormElement>) => void
  onFileChange: (file: File | null) => void
  onDownload: (id: string) => void
  onDelete: (id: string) => void
}) {
  return (
    <section className="product-detail inline-detail">
      <div className="product-preview">
        <div className="preview-media">
          {product.imageUrl ? <img src={product.imageUrl} alt="" /> : <span>Нет фото</span>}
        </div>
        <div>
          <p className="eyebrow">Карточка товара</p>
          <h2>{product.name}</h2>
          <dl>
            <div>
              <dt>Артикул</dt>
              <dd>{product.offerId}</dd>
            </div>
            <div>
              <dt>Product ID</dt>
              <dd>{product.productId}</dd>
            </div>
            <div>
              <dt>Статус</dt>
              <dd>{product.status}</dd>
            </div>
          </dl>
          {product.productUrl && (
            <a className="product-link" href={product.productUrl} target="_blank" rel="noreferrer">
              Открыть товар на Ozon
            </a>
          )}
        </div>
      </div>

      <form className="product-file-form" onSubmit={onUpload}>
        <input
          type="file"
          accept="image/*,.pdf,.xlsx,.xls,.doc,.docx"
          onChange={(event) => onFileChange(event.target.files?.[0] ?? null)}
        />
        <button type="submit">Добавить файл</button>
      </form>

      <div className="data-table">
        <div className="table-row file-row table-head">
          <span>Файл</span>
          <span>Дата</span>
          <span>Действия</span>
        </div>
        {files.map((file) => (
          <div className="table-row file-row" key={file.id}>
            <span>{file.fileName}</span>
            <span>{new Date(file.createdAt).toLocaleDateString('ru-RU')}</span>
            <span className="file-actions">
              <button type="button" onClick={() => onDownload(file.id)}>
                Скачать
              </button>
              {(userRole === 'Admin' || userRole === 'SuperAdmin') && (
                <button type="button" className="danger" onClick={() => onDelete(file.id)}>
                  Удалить
                </button>
              )}
            </span>
          </div>
        ))}
        {files.length === 0 && (
          <div className="empty-state">
            <strong>Для этого товара еще нет файлов производства.</strong>
          </div>
        )}
      </div>
    </section>
  )
}

function ProductionTaskTable({
  tasks,
  products,
  actualQuantities,
  setActualQuantities,
  onStart,
  onDefer,
  onComplete,
  onDelete,
  completed = false,
  deferred = false,
}: {
  tasks: ProductionTask[]
  products: OzonProduct[]
  actualQuantities: Record<string, string>
  setActualQuantities: Dispatch<SetStateAction<Record<string, string>>>
  onStart: (id: string) => void
  onDefer: (id: string) => void
  onComplete: (id: string) => void
  onDelete?: (id: string) => void
  completed?: boolean
  deferred?: boolean
}) {
  return (
    <div className="data-table">
      <div className="table-row task-row table-head">
        <span>Товар</span>
        <span>Артикул</span>
        <span>Нужно</span>
        <span>Факт</span>
        <span>Статус</span>
        <span>Исполнитель</span>
        <span></span>
      </div>
      {tasks.map((task) => {
        const taskItems = getProductionTaskItems(task)
        const isStaleDeferred =
          task.status === 'Deferred' &&
          task.deferredAt &&
          Date.now() - new Date(task.deferredAt).getTime() > 2 * 24 * 60 * 60 * 1000
        const isStaleNew =
          task.status === 'New' &&
          Date.now() - new Date(task.createdAt).getTime() > 4 * 60 * 60 * 1000

        return (
        <details
          className={`task-details-row ${isStaleDeferred ? 'deferred-stale' : ''} ${isStaleNew ? 'task-stale-new' : ''}`}
          key={task.id}
        >
          <summary className="table-row task-row">
          <span>
            <strong>{getProductionTaskSummary(task)}</strong>
            <small>
              {task.status === 'Deferred' && task.deferredAt
                ? `Отложена: ${new Date(task.deferredAt).toLocaleDateString('ru-RU')}`
                : new Date(task.createdAt).toLocaleDateString('ru-RU')}
            </small>
          </span>
          <span>{taskItems.map((item) => item.offerId || '-').join(', ')}</span>
          <span>{getProductionTaskRequiredTotal(task)}</span>
          <span>
            {completed ? (
              getProductionTaskActualTotal(task)
            ) : (
              <small>По товарам</small>
            )}
          </span>
          <span>{translateProductionTaskStatus(task.status)}</span>
          <span>{task.assignedUserName || '-'}</span>
          <span className="task-actions">
            {!completed && task.status === 'New' && (
              <button type="button" onClick={(event) => {
                event.preventDefault()
                onStart(task.id)
              }}>
                В работу
              </button>
            )}
            {!completed && task.status !== 'Deferred' && (
              <button type="button" onClick={(event) => {
                event.preventDefault()
                onDefer(task.id)
              }}>
                Отложить
              </button>
            )}
            {deferred && (
              <button type="button" onClick={(event) => {
                event.preventDefault()
                onStart(task.id)
              }}>
                В работу
              </button>
            )}
            {!completed && task.status === 'InProgress' && (
              <button type="button" onClick={(event) => {
                event.preventDefault()
                onComplete(task.id)
              }}>
                Завершить
              </button>
            )}
            {completed && onDelete && (
              <button type="button" className="danger" onClick={(event) => {
                event.preventDefault()
                onDelete(task.id)
              }}>
                Удалить
              </button>
            )}
          </span>
          </summary>
          <div className="task-items-table">
            <div className="table-row task-item-table-row table-head">
              <span>Товар</span>
              <span>Артикул</span>
              <span>План</span>
              <span>Факт</span>
            </div>
            {taskItems.map((item) => (
              <div className="table-row task-item-table-row" key={item.id}>
                <span className="product-mini task-product-mini">
                  <ProductThumb imageUrl={getTaskItemImageUrl(item, products)} name={item.productName} />
                  <span>
                    <strong>{item.productName}</strong>
                  </span>
                </span>
                <span>{item.offerId || '-'}</span>
                <span>{item.requiredQuantity}</span>
                <span>
                  {completed ? (
                    item.actualQuantity ?? 0
                  ) : (
                    <input
                      type="number"
                      min="0"
                      placeholder="Факт"
                      value={actualQuantities[item.id] ?? ''}
                      onChange={(event) =>
                        setActualQuantities((current) => ({
                          ...current,
                          [item.id]: event.target.value,
                        }))
                      }
                    />
                  )}
                </span>
              </div>
            ))}
          </div>
        </details>
        )
      })}
    </div>
  )
}

function getProductionTaskItems(task: ProductionTask) {
  if (task.items?.length) {
    return task.items
  }

  return [{
    id: task.id,
    ozonProductId: task.ozonProductId,
    offerId: task.offerId,
    productName: task.productName,
    requiredQuantity: task.requiredQuantity,
    actualQuantity: task.actualQuantity,
  }]
}

function getTaskItemImageUrl(item: ProductionTaskItem, products: OzonProduct[]) {
  return products.find((product) => product.productId === item.ozonProductId)?.imageUrl
}

function getProductionTaskRequiredTotal(task: ProductionTask) {
  return getProductionTaskItems(task).reduce((sum, item) => sum + item.requiredQuantity, 0)
}

function getProductionTaskActualTotal(task: ProductionTask) {
  return getProductionTaskItems(task).reduce((sum, item) => sum + (item.actualQuantity ?? 0), 0)
}

function getProductionTaskSummary(task: ProductionTask) {
  const items = getProductionTaskItems(task)
  return items.length === 1 ? items[0].productName : `${items.length} товаров в задаче`
}

function matchesProductionTask(task: ProductionTask, search: string) {
  return [
    task.offerId,
    task.productName,
    task.status,
    task.assignedUserName,
    task.requiredQuantity,
    task.actualQuantity,
    ...getProductionTaskItems(task).flatMap((item) => [
      item.offerId,
      item.productName,
      item.requiredQuantity,
      item.actualQuantity,
    ]),
  ]
    .filter((value) => value !== undefined && value !== null)
    .some((value) => String(value).toLowerCase().includes(search))
}

function matchesSupply(supply: Supply, search: string) {
  return [
    supply.id,
    supply.status,
    supply.createdAt,
    supply.sentAt,
    supply.acceptedAt,
    ...supply.items.flatMap((item) => [
      item.offerId,
      item.productName,
      item.quantity,
      item.isReserve ? 'резервный' : 'постоянный',
    ]),
  ]
    .filter((value) => value !== undefined && value !== null)
    .some((value) => String(value).toLowerCase().includes(search))
}

function ProductionTaskArchiveTable({
  tasks,
  products,
  onArchive,
  onDelete,
  emptyText = 'В архиве задач пока нет.',
}: {
  tasks: ProductionTask[]
  products: OzonProduct[]
  onArchive?: (id: string) => void
  onDelete?: (id: string) => void
  emptyText?: string
}) {
  return (
    <div className="data-table">
      <div className="table-row task-archive-row table-head">
        <span>Что было в задаче</span>
        <span>Артикул</span>
        <span>План</span>
        <span>Факт</span>
        <span>Кто выполнял</span>
        <span>Взял в работу</span>
        <span>Завершил</span>
        <span></span>
      </div>
      {tasks.map((task) => (
        <details className="task-details-row" key={task.id}>
          <summary className="table-row task-archive-row">
          <span>
            <strong>{getProductionTaskSummary(task)}</strong>
            <small>Создана: {formatDateTime(task.createdAt)}</small>
          </span>
          <span>{getProductionTaskItems(task).map((item) => item.offerId || '-').join(', ')}</span>
          <span>{getProductionTaskRequiredTotal(task)}</span>
          <span>{getProductionTaskActualTotal(task)}</span>
          <span>{task.assignedUserName || '-'}</span>
          <span>{task.startedAt ? formatDateTime(task.startedAt) : '-'}</span>
          <span>{task.completedAt ? formatDateTime(task.completedAt) : '-'}</span>
          <span className="task-actions">
            {onArchive && (
              <button type="button" onClick={(event) => {
                event.preventDefault()
                onArchive(task.id)
              }}>
                Архивировать
              </button>
            )}
            {onDelete && (
              <button type="button" className="danger" onClick={(event) => {
                event.preventDefault()
                onDelete(task.id)
              }}>
                Удалить из архива
              </button>
            )}
          </span>
          </summary>
          <div className="task-items-table">
            <div className="table-row task-item-table-row table-head">
              <span>Товар</span>
              <span>Артикул</span>
              <span>План</span>
              <span>Факт</span>
            </div>
            {getProductionTaskItems(task).map((item) => (
              <div className="table-row task-item-table-row" key={item.id}>
                <span className="product-mini task-product-mini">
                  <ProductThumb imageUrl={getTaskItemImageUrl(item, products)} name={item.productName} />
                  <span>
                    <strong>{item.productName}</strong>
                  </span>
                </span>
                <span>{item.offerId || '-'}</span>
                <span>{item.requiredQuantity}</span>
                <span>{item.actualQuantity ?? 0}</span>
              </div>
            ))}
          </div>
        </details>
      ))}
      {tasks.length === 0 && (
        <div className="empty-state">
          <strong>{emptyText}</strong>
        </div>
      )}
    </div>
  )
}

function SupplyTable({
  supplies,
  ozonProducts,
  productSupplierLinks,
  replaceProducts,
  setReplaceProducts,
  editingSupplyId,
  editSupplyItems,
  setEditSupplyItems,
  editSupplyProductId,
  setEditSupplyProductId,
  editSupplyQuantity,
  setEditSupplyQuantity,
  editReserveProductName,
  setEditReserveProductName,
  editReserveQuantity,
  setEditReserveQuantity,
  editReserveSupplierName,
  setEditReserveSupplierName,
  editReserveSupplierUrl,
  setEditReserveSupplierUrl,
  onStartEdit,
  onCancelEdit,
  onAddEditProduct,
  onAddEditReserve,
  onSaveEdit,
  onDeleteSupply,
  onArchiveSupply,
  onStatusChange,
  onDownloadOrder,
  supplyActualOrderEdits,
  setSupplyActualOrderEdits,
  onActualOrderChange,
  userRole,
  hideItemsUntilEdit = false,
  archiveMode = false,
}: {
  supplies: Supply[]
  ozonProducts: OzonProduct[]
  productSupplierLinks: Record<number, ProductSupplierLink[]>
  replaceProducts: Record<string, string>
  setReplaceProducts: Dispatch<SetStateAction<Record<string, string>>>
  editingSupplyId: string | null
  editSupplyItems: DraftSupplyItem[]
  setEditSupplyItems: Dispatch<SetStateAction<DraftSupplyItem[]>>
  editSupplyProductId: string
  setEditSupplyProductId: Dispatch<SetStateAction<string>>
  editSupplyQuantity: string
  setEditSupplyQuantity: Dispatch<SetStateAction<string>>
  editReserveProductName: string
  setEditReserveProductName: Dispatch<SetStateAction<string>>
  editReserveQuantity: string
  setEditReserveQuantity: Dispatch<SetStateAction<string>>
  editReserveSupplierName: string
  setEditReserveSupplierName: Dispatch<SetStateAction<string>>
  editReserveSupplierUrl: string
  setEditReserveSupplierUrl: Dispatch<SetStateAction<string>>
  onStartEdit: (supply: Supply) => void
  onCancelEdit: () => void
  onAddEditProduct: () => void
  onAddEditReserve: () => void
  onSaveEdit: (id: string) => void
  onDeleteSupply: (id: string) => void
  onArchiveSupply: (id: string) => void
  onStatusChange: (id: string, status: SupplyStatus) => void
  onDownloadOrder: (id: string) => void
  supplyActualOrderEdits: Record<string, string>
  setSupplyActualOrderEdits: Dispatch<SetStateAction<Record<string, string>>>
  onActualOrderChange: (itemId: string, fallbackQuantity: number) => void
  userRole?: string
  hideItemsUntilEdit?: boolean
  archiveMode?: boolean
}) {
  const [expandedArchiveSupplyIds, setExpandedArchiveSupplyIds] = useState<Record<string, boolean>>({})
  const purchaseProducts = ozonProducts.filter((item) => item.productType === 'Purchase')

  return (
    <div className="supply-list">
      {supplies.map((supply) => {
        const isEditing = editingSupplyId === supply.id
        const canEdit = !archiveMode && ((userRole === 'Admin' || userRole === 'SuperAdmin') || supply.status === 'Created')
        const isArchiveExpanded = expandedArchiveSupplyIds[supply.id] ?? false
        const showItems = (archiveMode && isArchiveExpanded) || isEditing || (!archiveMode && !hideItemsUntilEdit)
        const allItemsOrdered = supply.items.length > 0 && supply.items.every((item) => item.actualOrderQuantity > 0)
        const rows: DraftSupplyItem[] = isEditing
          ? editSupplyItems
          : supply.items.map((item) => ({
              tempId: item.id,
              id: item.id,
              ozonProductId: item.ozonProductId,
              offerId: item.offerId,
              productName: item.productName,
              quantity: item.quantity,
              actualOrderQuantity: item.actualOrderQuantity,
              supplierName: item.supplierName,
              supplierUrl: item.supplierUrl,
              isReserve: item.isReserve,
            }))

        return (
          <section className="supply-card" key={supply.id}>
            <div className="supply-card-head">
              <span>
                <strong>Поставка от {formatDateTime(supply.createdAt)}</strong>
                <small>
                  Отгрузка: {supply.sentAt ? formatDateTime(supply.sentAt) : '-'} | Приемка:{' '}
                  {supply.acceptedAt ? formatDateTime(supply.acceptedAt) : '-'}
                </small>
              </span>
              <span className="status-pill">{translateSupplyStatus(supply.status)}</span>
              {(canEdit || archiveMode) && (
                <span className="supply-status-actions">
                  {!archiveMode && supply.status === 'Created' && allItemsOrdered && (
                    <>
                      <button type="button" onClick={() => onStatusChange(supply.id, 'Sent')}>
                        Отправлено
                      </button>
                      <button type="button" onClick={() => onDownloadOrder(supply.id)}>
                        Скачать Excel заказа
                      </button>
                    </>
                  )}
                  {(userRole === 'Admin' || userRole === 'SuperAdmin') && !archiveMode && supply.status === 'Sent' && (
                    <>
                      <button type="button" onClick={() => onStatusChange(supply.id, 'Accepted')}>
                        Принято
                      </button>
                    </>
                  )}
                  {isEditing ? (
                    <>
                      <button type="button" onClick={() => onSaveEdit(supply.id)}>
                        Сохранить
                      </button>
                      <button type="button" onClick={onCancelEdit}>
                        Отмена
                      </button>
                    </>
                  ) : (
                    <button type="button" onClick={() => onStartEdit(supply)}>
                      Редактировать
                    </button>
                  )}
                  {(userRole === 'Admin' || userRole === 'SuperAdmin') && !archiveMode && (
                    <button type="button" onClick={() => onArchiveSupply(supply.id)}>
                      Архивировать
                    </button>
                  )}
                  {(userRole === 'Admin' || userRole === 'SuperAdmin') && archiveMode && (
                    <>
                      <button
                        type="button"
                        onClick={() =>
                          setExpandedArchiveSupplyIds((current) => ({
                            ...current,
                            [supply.id]: !isArchiveExpanded,
                          }))
                        }
                      >
                        {isArchiveExpanded ? 'Свернуть товары' : 'Показать товары'}
                      </button>
                      <button
                        type="button"
                        className="danger"
                        onClick={() => onDeleteSupply(supply.id)}
                      >
                        Удалить из архива
                      </button>
                    </>
                  )}
                </span>
              )}
            </div>

            {(userRole === 'Admin' || userRole === 'SuperAdmin') && (
              <details className="supply-history">
                <summary>История изменений</summary>
                <div className="supply-history-list">
                  {supply.history?.map((item) => (
                    <div className="supply-history-row" key={item.id}>
                      <span>
                        <strong>{item.action}</strong>
                        <small>{item.details || '-'}</small>
                      </span>
                      <span>
                        <strong>{item.displayName || item.userName || '-'}</strong>
                        <small>{formatDateTime(item.createdAt)}</small>
                      </span>
                    </div>
                  ))}
                  {(!supply.history || supply.history.length === 0) && (
                    <div className="empty-state">Истории по этой поставке пока нет.</div>
                  )}
                </div>
              </details>
            )}

            {isEditing && (
              <div className="supply-edit-tools">
                <div className="supply-form-block">
                  <strong>Добавить товар из Ozon</strong>
                  <ProductSearchInput
                    listId={`edit-supply-products-${supply.id}`}
                    products={purchaseProducts}
                    supplierLinks={productSupplierLinks}
                    selectedProductId={editSupplyProductId}
                    onProductIdChange={setEditSupplyProductId}
                    placeholder="Начните писать название или артикул"
                  />
                  <input
                    type="number"
                    min="1"
                    placeholder="Количество"
                    value={editSupplyQuantity}
                    onChange={(event) => setEditSupplyQuantity(event.target.value)}
                  />
                  <button type="button" onClick={onAddEditProduct}>
                    Добавить
                  </button>
                </div>

                <div className="supply-form-block">
                  <strong>Добавить резервный товар</strong>
                  <input
                    placeholder="Название"
                    value={editReserveProductName}
                    onChange={(event) => setEditReserveProductName(event.target.value)}
                  />
                  <input
                    type="number"
                    min="1"
                    placeholder="Количество"
                    value={editReserveQuantity}
                    onChange={(event) => setEditReserveQuantity(event.target.value)}
                  />
                  <input
                    placeholder="Поставщик"
                    value={editReserveSupplierName}
                    onChange={(event) => setEditReserveSupplierName(event.target.value)}
                  />
                  <input
                    placeholder="Ссылка поставщика"
                    value={editReserveSupplierUrl}
                    onChange={(event) => setEditReserveSupplierUrl(event.target.value)}
                  />
                  <button type="button" onClick={onAddEditReserve}>
                    Добавить резерв
                  </button>
                </div>
              </div>
            )}

            {showItems && (
              <div className="data-table">
                <div className="table-row supply-item-row table-head">
                  <span>Товар</span>
                  <span>Артикул</span>
                  <span>Количество</span>
                  <span>Факт заказа</span>
                  <span>Поставщик</span>
                  <span>Тип</span>
                  <span>{isEditing ? 'Действия' : 'Статус заказа'}</span>
                </div>
                {rows.map((item) => (
                  <div className="table-row supply-item-row" key={isEditing ? item.tempId : item.id}>
                    <span>
                      {isEditing && item.isReserve ? (
                        <input
                          value={item.productName}
                          onChange={(event) =>
                            setEditSupplyItems((current) =>
                              current.map((row) =>
                                row.tempId === item.tempId
                                  ? { ...row, productName: event.target.value }
                                  : row,
                              ),
                            )
                          }
                        />
                      ) : (
                        item.productName
                      )}
                    </span>
                    <span>{item.offerId || '-'}</span>
                    <span>
                      {isEditing ? (
                        <input
                          type="number"
                          min="1"
                          value={item.quantity}
                          onChange={(event) =>
                            setEditSupplyItems((current) =>
                              current.map((row) =>
                                row.tempId === item.tempId
                                  ? { ...row, quantity: Number(event.target.value) }
                                  : row,
                              ),
                            )
                          }
                        />
                      ) : (
                        item.quantity
                      )}
                    </span>
                    <span className="supply-actual-cell">
                      {isEditing ? (
                        <input
                          type="number"
                          min="0"
                          value={item.actualOrderQuantity}
                          onChange={(event) =>
                            setEditSupplyItems((current) =>
                              current.map((row) =>
                                row.tempId === item.tempId
                                  ? { ...row, actualOrderQuantity: Number(event.target.value) }
                                  : row,
                              ),
                            )
                          }
                        />
                      ) : item.id && !archiveMode ? (
                        <>
                          <input
                            type="number"
                            min="0"
                            value={supplyActualOrderEdits[item.id] ?? String(item.actualOrderQuantity)}
                            onChange={(event) =>
                              setSupplyActualOrderEdits((current) => ({
                                ...current,
                                [item.id ?? '']: event.target.value,
                              }))
                            }
                          />
                          <button
                            type="button"
                            onClick={() => item.id && onActualOrderChange(item.id, item.actualOrderQuantity)}
                          >
                            Сохранить
                          </button>
                        </>
                      ) : (
                        item.actualOrderQuantity
                      )}
                    </span>
                    <span>
                      {isEditing && item.isReserve ? (
                        <span className="reserve-supplier-fields">
                          <input
                            value={item.supplierName}
                            placeholder="Поставщик"
                            onChange={(event) =>
                              setEditSupplyItems((current) =>
                                current.map((row) =>
                                  row.tempId === item.tempId
                                    ? { ...row, supplierName: event.target.value }
                                    : row,
                                ),
                              )
                            }
                          />
                          <input
                            value={item.supplierUrl}
                            placeholder="Ссылка"
                            onChange={(event) =>
                              setEditSupplyItems((current) =>
                                current.map((row) =>
                                  row.tempId === item.tempId
                                    ? { ...row, supplierUrl: event.target.value }
                                    : row,
                                ),
                              )
                            }
                          />
                        </span>
                      ) : item.supplierUrl ? (
                        <a href={item.supplierUrl} target="_blank" rel="noreferrer">
                          {item.supplierName || 'Поставщик'}
                        </a>
                      ) : item.ozonProductId && (productSupplierLinks[item.ozonProductId] ?? []).length > 0 ? (
                        <span className="selected-supplier-links">
                          {(productSupplierLinks[item.ozonProductId] ?? []).map((link) => (
                            <a href={link.supplierUrl} target="_blank" rel="noreferrer" key={link.id ?? link.supplierUrl}>
                              {link.supplierName || 'Поставщик'}
                            </a>
                          ))}
                        </span>
                      ) : (
                        '-'
                      )}
                    </span>
                    <span>{item.isReserve ? 'Резервный' : 'Постоянный'}</span>
                    <span className="reserve-replace">
                      {isEditing ? (
                        <>
                          {item.isReserve && (
                            <>
                              <ProductSearchInput
                                listId={`edit-replace-products-${item.tempId}`}
                                products={purchaseProducts}
                                supplierLinks={productSupplierLinks}
                                selectedProductId={replaceProducts[item.tempId] ?? ''}
                                onProductIdChange={(productId) =>
                                  setReplaceProducts((current) => ({
                                    ...current,
                                    [item.tempId]: productId,
                                  }))
                                }
                                placeholder="Найти постоянный товар"
                              />
                              <button
                                type="button"
                                onClick={() => {
                                  const product = purchaseProducts.find(
                                    (product) =>
                                      String(product.productId) === replaceProducts[item.tempId],
                                  )

                                  if (!product) {
                                    return
                                  }

                                  setEditSupplyItems((current) =>
                                    current.map((row) =>
                                      row.tempId === item.tempId
                                        ? {
                                            ...row,
                                            ozonProductId: product.productId,
                                            offerId: product.offerId,
                                            productName: product.name,
                                            isReserve: false,
                                          }
                                        : row,
                                    ),
                                  )
                                  setReplaceProducts((current) => ({
                                    ...current,
                                    [item.tempId]: '',
                                  }))
                                }}
                              >
                                Заменить
                              </button>
                            </>
                          )}
                          <button
                            type="button"
                            className="danger"
                            onClick={() =>
                              setEditSupplyItems((current) =>
                                current.filter((row) => row.tempId !== item.tempId),
                              )
                            }
                          >
                            Удалить строку
                          </button>
                        </>
                      ) : (
                        <span className={`order-status-pill ${item.actualOrderQuantity > 0 ? 'is-ordered' : 'is-not-ordered'}`}>
                          {item.actualOrderQuantity > 0 ? 'Заказано' : 'Не заказано'}
                        </span>
                      )}
                    </span>
                  </div>
                ))}
                {rows.length === 0 && (
                  <div className="empty-state">
                    <strong>В поставке нет товаров.</strong>
                  </div>
                )}
              </div>
            )}
          </section>
        )
      })}
      {supplies.length === 0 && (
        <div className="empty-state">
          <strong>Поставок пока нет.</strong>
        </div>
      )}
    </div>
  )
}

function SupplyAnalyticsTable({ rows }: { rows: SupplyAnalyticsItem[] }) {
  return (
    <div className="data-table">
      <div className="table-row supply-analytics-row table-head">
        <span>Товар</span>
        <span>Артикул</span>
        <span>Количество</span>
        <span>Статус</span>
        <span>Создано</span>
        <span>Отправлено</span>
        <span>Принято</span>
      </div>
      {rows.map((row) => (
        <div className="table-row supply-analytics-row" key={`${row.supplyId}-${row.id}`}>
          <span>
            <strong>{row.productName}</strong>
            <small>{row.isReserve ? 'Резервный товар' : 'Постоянный товар'}</small>
          </span>
          <span>{row.offerId || '-'}</span>
          <span>{row.quantity}</span>
          <span>{translateSupplyStatus(row.status)}</span>
          <span>{formatDateTime(row.createdAt)}</span>
          <span>{row.sentAt ? formatDateTime(row.sentAt) : '-'}</span>
          <span>{row.acceptedAt ? formatDateTime(row.acceptedAt) : '-'}</span>
        </div>
      ))}
      {rows.length === 0 && (
        <div className="empty-state">
          <strong>По этому товару поставок пока нет.</strong>
        </div>
      )}
    </div>
  )
}

function AllSuppliesTable({ supplies }: { supplies: Supply[] }) {
  return (
    <div className="all-supplies-list">
      {supplies.map((supply) => {
        const totalQuantity = supply.items.reduce((sum, item) => sum + item.quantity, 0)

        return (
          <details className="all-supply-card" key={supply.id}>
            <summary>
              <span>
                <strong>Поставка от {formatDateTime(supply.createdAt)}</strong>
                <small>
                  Отгрузка: {supply.sentAt ? formatDateTime(supply.sentAt) : '-'} | Приемка:{' '}
                  {supply.acceptedAt ? formatDateTime(supply.acceptedAt) : '-'}
                </small>
              </span>
              <span className="status-pill">{translateSupplyStatus(supply.status)}</span>
              <span>
                <strong>{totalQuantity}</strong>
                <small>шт. всего</small>
              </span>
            </summary>

            <div className="data-table">
              <div className="table-row all-supply-item-row table-head">
                <span>Товар</span>
                <span>Артикул</span>
                <span>Количество</span>
                <span>Тип</span>
              </div>
              {supply.items.map((item) => (
                <div className="table-row all-supply-item-row" key={item.id}>
                  <span>{item.productName}</span>
                  <span>{item.offerId || '-'}</span>
                  <span>{item.quantity}</span>
                  <span>{item.isReserve ? 'Резервный' : 'Постоянный'}</span>
                </div>
              ))}
            </div>
          </details>
        )
      })}
      {supplies.length === 0 && (
        <div className="empty-state">
          <strong>Поставок пока нет.</strong>
        </div>
      )}
    </div>
  )
}

function StockRow({
  item,
  urgentInfo,
  priceValue,
  onPriceChange,
  onSave,
  canEditPrice,
}: {
  item: OzonStock
  urgentInfo?: UrgentStockItem
  priceValue: string
  onPriceChange: (value: string) => void
  onSave: () => void
  canEditPrice: boolean
}) {
  return (
    <div className="table-row stock-row">
      <span data-label="Товар">
        <strong>{item.name}</strong>
        {urgentInfo && (
          <small className="urgent-stock-note">
            Срочно: хватит на {formatDaysLeft(urgentInfo.daysLeft)}, продажи {urgentInfo.dailySales.toFixed(2)}/день
          </small>
        )}
        {item.productUrl && (
          <a href={item.productUrl} target="_blank" rel="noreferrer">
            Открыть Ozon
          </a>
        )}
      </span>
      <span data-label="Артикул">{item.offerId}</span>
      <span data-label="FBO">{item.fboPresent}</span>
      <span data-label="FBS">{item.fbsPresent}</span>
      <span className="stock-price-cell" data-label="Цена">
        <input
          value={priceValue}
          onChange={(event) => onPriceChange(event.target.value)}
          disabled={!canEditPrice}
        />
      </span>
      <span className="stock-save-cell" data-label="Действие">
        <button type="button" onClick={onSave} disabled={!canEditPrice}>
          Сохранить
        </button>
      </span>
    </div>
  )
}

type UrgentStockItem = OzonStock & {
  dailySales: number
  daysLeft: number
}

function getStockSortValue(item: OzonStock, key: 'total' | 'fbo' | 'fbs') {
  if (key === 'fbo') {
    return item.fboPresent
  }

  if (key === 'fbs') {
    return item.fbsPresent
  }

  return item.fboPresent + item.fbsPresent
}

function getUrgentSupplyQuantity(item: UrgentStockItem) {
  const totalStock = item.fboPresent + item.fbsPresent
  const targetStock = Math.ceil(item.dailySales * 14)
  return Math.max(1, targetStock - totalStock)
}

function getUrgentStockItems(stocks: OzonStock[], analytics: OzonAnalytics | null): UrgentStockItem[] {
  if (!analytics) {
    return []
  }

  const salesByKey = new Map<string, number>()
  for (const row of analytics.topProducts) {
    if (row.sku) {
      salesByKey.set(`sku:${row.sku}`, (salesByKey.get(`sku:${row.sku}`) ?? 0) + row.quantity)
    }
    if (row.offerId) {
      salesByKey.set(`offer:${row.offerId}`, (salesByKey.get(`offer:${row.offerId}`) ?? 0) + row.quantity)
    }
  }

  return stocks
    .map((stock) => {
      const soldQuantity =
        (stock.sku ? salesByKey.get(`sku:${stock.sku}`) : undefined) ?? salesByKey.get(`offer:${stock.offerId}`) ?? 0
      const dailySales = soldQuantity / 28
      const totalStock = stock.fboPresent + stock.fbsPresent
      const daysLeft = dailySales > 0 ? totalStock / dailySales : Number.POSITIVE_INFINITY

      return {
        ...stock,
        dailySales,
        daysLeft,
      }
    })
    .filter((stock) => stock.dailySales > 0 && stock.daysLeft <= 5)
    .sort((left, right) => left.daysLeft - right.daysLeft)
}

function formatDaysLeft(value: number) {
  if (!Number.isFinite(value)) {
    return 'не рассчитано'
  }

  if (value < 1) {
    return 'меньше 1 дня'
  }

  return `${Math.ceil(value)} дн.`
}

function getApiErrorMessage(errorText: string, fallback: string) {
  if (!errorText.trim()) {
    return fallback
  }

  try {
    const data = JSON.parse(errorText) as { detail?: string; title?: string; message?: string }
    return data.detail || data.message || data.title || fallback
  } catch {
    return errorText.length > 180 ? `${errorText.slice(0, 180)}...` : errorText
  }
}

function formatMoney(value: number, _currency?: string) {
  return new Intl.NumberFormat('ru-RU', {
    style: 'decimal',
    maximumFractionDigits: 2,
    minimumFractionDigits: 0,
  }).format(value)
}

function formatPlainNumber(value: number) {
  return new Intl.NumberFormat('ru-RU', {
    maximumFractionDigits: 2,
  }).format(value)
}

function buildDashboardSalesDays(analytics: OzonAnalytics | null) {
  const today = new Date()
  const valuesByDate = new Map((analytics?.dailySales ?? []).map((item) => [item.date, item]))

  return Array.from({ length: 30 }, (_, index) => {
    const date = new Date(today)
    date.setDate(today.getDate() - (29 - index))
    const key = date.toISOString().slice(0, 10)
    const value = valuesByDate.get(key)
    return {
      date: key,
      quantity: value?.quantity ?? 0,
      revenue: value?.revenue ?? 0,
    }
  })
}

function formatDateShort(value: string) {
  return new Date(value).toLocaleDateString('ru-RU', {
    day: '2-digit',
    month: '2-digit',
  })
}

function formatProductType(value: ProductType) {
  if (value === 'Production') {
    return 'Производственный'
  }

  if (value === 'Purchase') {
    return 'Закупочный'
  }

  return 'Не настроен'
}

function translateStatus(status: string) {
  const statuses: Record<string, string> = {
    awaiting_deliver: 'Собирается',
    delivering: 'Едет',
    delivered: 'Доставлен',
    cancelled: 'Отменен',
  }

  return statuses[status] ?? status
}

function translateProductionTaskStatus(status: ProductionTask['status']) {
  const statuses: Record<ProductionTask['status'], string> = {
    New: 'Новая',
    InProgress: 'В работе',
    Deferred: 'Отложена',
    Completed: 'Выполнено',
  }

  return statuses[status] ?? status
}

function translateSupplyStatus(status: SupplyStatus) {
  const statuses: Record<SupplyStatus, string> = {
    Created: 'Создано',
    Sent: 'Отправлено',
    Accepted: 'Принято',
  }

  return statuses[status] ?? status
}

function formatDateTime(value: string) {
  return new Date(value).toLocaleString('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function getRemainingDays(value?: string) {
  if (!value) {
    return null
  }

  const diffMs = new Date(value).getTime() - Date.now()
  if (!Number.isFinite(diffMs)) {
    return null
  }

  return Math.max(0, Math.ceil(diffMs / (1000 * 60 * 60 * 24)))
}

function getTrialRemainingText(value?: string) {
  const days = getRemainingDays(value)
  if (days === null) {
    return 'Пробный период не задан'
  }

  return days > 0 ? `До конца пробного периода: ${days} дн.` : 'Пробный период закончился'
}

function getSubscriptionRemainingText(value?: string) {
  const days = getRemainingDays(value)
  if (days === null) {
    return 'Оплаченной подписки пока нет'
  }

  return days > 0 ? `До конца оплаченного месяца: ${days} дн.` : 'Оплаченный месяц закончился'
}

async function prepareAvatarFile(file: File) {
  const maxSize = 900 * 1024
  const maxSide = 512

  if (file.size <= maxSize && file.type !== 'image/png') {
    return file
  }

  const dataUrl = await readFileAsDataUrl(file)
  const image = await loadImage(dataUrl)
  const scale = Math.min(1, maxSide / Math.max(image.naturalWidth, image.naturalHeight))
  const width = Math.max(1, Math.round(image.naturalWidth * scale))
  const height = Math.max(1, Math.round(image.naturalHeight * scale))
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const context = canvas.getContext('2d')
  if (!context) {
    return file
  }

  context.drawImage(image, 0, 0, width, height)
  const blob = await canvasToBlob(canvas, 'image/jpeg', 0.86)
  if (!blob) {
    return file
  }

  if (file.size <= maxSize && blob.size >= file.size) {
    return file
  }

  const baseName = file.name.replace(/\.[^.]+$/, '') || 'avatar'
  return new File([blob], `${baseName}.jpg`, { type: 'image/jpeg' })
}

function readFileAsDataUrl(file: File) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(reader.error)
    reader.readAsDataURL(file)
  })
}

function loadImage(src: string) {
  return new Promise<HTMLImageElement>((resolve, reject) => {
    const image = new Image()
    image.onload = () => resolve(image)
    image.onerror = reject
    image.src = src
  })
}

function canvasToBlob(canvas: HTMLCanvasElement, type: string, quality: number) {
  return new Promise<Blob | null>((resolve) => {
    canvas.toBlob(resolve, type, quality)
  })
}

export default App
