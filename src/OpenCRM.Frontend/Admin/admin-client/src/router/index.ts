import { createRouter, createWebHashHistory } from 'vue-router'
import DashboardView from '@/views/DashboardView.vue'
import CrmImportView from '@/views/CrmImportView.vue'
import BillingView from '@/views/BillingView.vue'
import NotFoundView from '@/views/NotFoundView.vue'
import PromoCodesView from '@/views/PromoCodesView.vue'
import SubscriptionPlansView from '@/views/SubscriptionPlansView.vue'
import SystemRolesView from '@/views/SystemRolesView.vue'
import UserOverviewView from '@/views/UserOverviewView.vue'
import UsersView from '@/views/UsersView.vue'
import OrganizationsView from '@/views/OrganizationsView.vue'
import CatalogSuggestionsView from '@/views/CatalogSuggestionsView.vue'
import EmailCampaignsView from '@/views/EmailCampaignsView.vue'
import EmailComposeView from '@/views/EmailComposeView.vue'
import EmailMessagesView from '@/views/EmailMessagesView.vue'
import SiteNotificationsView from '@/views/SiteNotificationsView.vue'
import FeaturesView from '@/views/FeaturesView.vue'
import SmsHistoryView from '@/views/SmsHistoryView.vue'
import SupportMessagesView from '@/views/SupportMessagesView.vue'
import TestDataView from '@/views/TestDataView.vue'
import VehicleCatalogView from '@/views/VehicleCatalogView.vue'
import AssistantView from '@/views/AssistantView.vue'
import { pinia } from '@/stores/pinia'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'

const permissionRoutes = [
  { permission: 'Admin.Dashboard.View', path: '/' },
  { permission: 'Admin.Users.View', path: '/users' },
  { permission: 'Admin.Billing.View', path: '/finance/billing' },
  { permission: 'Admin.Configuration.Manage', path: '/finance/subscription-plans' },
  { permission: 'Admin.Content.View', path: '/content/support' },
  { permission: 'Admin.Communications.View', path: '/communications/emails' }
]

const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    {
      path: '/',
      name: 'dashboard',
      component: DashboardView,
      meta: {
        requiresPermission: 'Admin.Dashboard.View'
      }
    },
    {
      path: '/organizations',
      name: 'organizations',
      component: OrganizationsView,
      meta: { requiresPermission: 'Admin.Users.View' }
    },
    {
      path: '/users',
      name: 'users',
      component: UsersView,
      meta: {
        requiresPermission: 'Admin.Users.View'
      }
    },
    {
      path: '/users/:userId',
      name: 'user-overview',
      component: UserOverviewView,
      meta: {
        requiresPermission: 'Admin.Users.View'
      }
    },
    {
      path: '/finance/billing',
      name: 'billing',
      component: BillingView,
      meta: {
        requiresPermission: 'Admin.Billing.View',
        requiresCommercial: true
      }
    },
    {
      path: '/finance/billing/:recordType/:recordId',
      name: 'billing-record',
      component: BillingView,
      meta: {
        requiresPermission: 'Admin.Billing.View',
        requiresCommercial: true
      }
    },
    {
      path: '/finance/subscription-plans',
      name: 'subscription-plans',
      component: SubscriptionPlansView,
      meta: {
        requiresPermission: 'Admin.Configuration.Manage',
        requiresCommercial: true
      }
    },
    {
      path: '/finance/promo-codes',
      name: 'promo-codes',
      component: PromoCodesView,
      meta: {
        requiresPermission: 'Admin.Configuration.Manage',
        requiresCommercial: true
      }
    },
    {
      path: '/configuration/features',
      component: FeaturesView,
      meta: { requiresPermission: 'Admin.Configuration.Manage' }
    },
    {
      path: '/assistant',
      name: 'assistant',
      component: AssistantView,
      meta: { requiresPermission: 'Admin.Configuration.Manage' }
    },
    {
      path: '/configuration/vehicle-catalog',
      component: VehicleCatalogView,
      meta: { requiresPermission: 'Admin.Content.Manage' }
    },
    {
      path: '/content/support',
      component: SupportMessagesView,
      meta: { requiresPermission: 'Admin.Content.View' }
    },
    {
      path: '/content/support/:messageId',
      component: SupportMessagesView,
      meta: { requiresPermission: 'Admin.Content.View' }
    },
    {
      path: '/content/catalog-suggestions',
      component: CatalogSuggestionsView,
      meta: { requiresPermission: 'Admin.Content.View' }
    },
    {
      path: '/communications/emails',
      component: EmailMessagesView,
      meta: { requiresPermission: 'Admin.Communications.View' }
    },
    {
      path: '/communications/email-compose',
      component: EmailComposeView,
      meta: { requiresPermission: 'Admin.Communications.Manage' }
    },
    {
      path: '/communications/email-campaigns',
      component: EmailCampaignsView,
      meta: { requiresPermission: 'Admin.Communications.Manage' }
    },
    {
      path: '/communications/sms',
      component: SmsHistoryView,
      meta: { requiresPermission: 'Admin.Communications.View' }
    },
    {
      path: '/communications/site-notifications',
      component: SiteNotificationsView,
      meta: { requiresPermission: 'Admin.Communications.View' }
    },
    {
      path: '/system/admin-roles',
      name: 'system-admin-roles',
      component: SystemRolesView,
      meta: {
        requiresSystem: true
      }
    },
    {
      path: '/system/test-data',
      component: TestDataView,
      meta: { requiresSystem: true }
    },
    {
      path: '/system/crm-import',
      name: 'crm-import',
      component: CrmImportView,
      meta: { requiresSystem: true }
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: NotFoundView
    }
  ]
})

router.beforeEach(async to => {
  const requiredPermission = to.meta.requiresPermission?.toString()
  if (!to.meta.requiresSystem && !requiredPermission) {
    return true
  }

  const currentUserStore = useAdminCurrentUserStore(pinia)
  const currentUser = await currentUserStore.load()

  if (to.meta.requiresSystem) {
    return currentUser?.isSystem === true ? true : { name: 'dashboard' }
  }

  if (to.meta.requiresCommercial && currentUser?.commercialEnabled !== true) {
    return { name: 'not-found' }
  }

  if (!requiredPermission) {
    return false
  }

  if (currentUser?.isSystem === true || currentUser?.permissions.includes(requiredPermission) === true) {
    return true
  }

  const fallbackRoute = permissionRoutes.find(route => currentUser?.permissions.includes(route.permission) === true)
  if (!fallbackRoute || fallbackRoute.path === to.path) {
    return false
  }

  return { path: fallbackRoute.path }
})

export default router
