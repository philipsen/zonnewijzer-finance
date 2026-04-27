import { createRouter, createWebHistory } from 'vue-router'
import Home from '../components/Home.vue'
import UsersList from '../components/UsersList.vue'
import EditUser from '../components/EditUser.vue'
import ChargingSessionsList from '../components/ChargingSessionsList.vue'
import MyChargingSessions from '../components/MyChargingSessions.vue'
import ChargingUsersList from '../components/ChargingUsersList.vue'
import EditChargingUser from '../components/EditChargingUser.vue'
import InvoiceGrid from '../components/InvoiceGrid.vue'
import MyInvoices from '../components/MyInvoices.vue'
import PaymentsList from '../components/PaymentsList.vue'
import PaymentReturn from '../components/PaymentReturn.vue'
import ResetPassword from '../components/ResetPassword.vue'
import EventLog from '../components/EventLog.vue'
import EnergyPrices from '../components/EnergyPrices.vue'
import ChargingStationsPage from '../components/ChargingStationsPage.vue'
import AdminSigenergyState from '../components/AdminSigenergyState.vue'
import { isAppAdmin, roleFromToken, canManageUserProfiles } from '../utils/roles'

function requireManageUserProfiles () {
  const token = localStorage.getItem('token')
  const role = roleFromToken(token)
  if (!canManageUserProfiles(role)) return '/'
}

const routes = [
  {
    path: '/',
    name: 'Home',
    component: Home
  },
  {
    path: '/users',
    name: 'Users',
    component: UsersList,
    beforeEnter: requireManageUserProfiles
  },
  {
    path: '/users/:id/edit',
    name: 'EditUser',
    component: EditUser,
    beforeEnter: requireManageUserProfiles
  },
  {
    path: '/upload-sessions',
    redirect: '/'
  },
  {
    path: '/charging-sessions',
    name: 'ChargingSessionsList',
    component: ChargingSessionsList
  },
  {
    path: '/charging-stations',
    name: 'ChargingStations',
    component: ChargingStationsPage,
    beforeEnter: () => {
      const token = localStorage.getItem('token')
      const role = roleFromToken(token)
      if (!isAppAdmin(role)) return '/'
    }
  },
  {
    path: '/my-sessions',
    name: 'MyChargingSessions',
    component: MyChargingSessions
  },
  {
    path: '/charge-cards',
    name: 'ChargeCardsList',
    component: ChargingUsersList
  },
  {
    path: '/charge-cards/:id/edit',
    name: 'EditChargeCard',
    component: EditChargingUser
  },
  {
    path: '/charging-users',
    redirect: '/charge-cards'
  },
  {
    path: '/charging-users/:id/edit',
    redirect: (to) => ({ path: `/charge-cards/${to.params.id}/edit` })
  },
  {
    path: '/invoices',
    name: 'InvoiceGrid',
    component: InvoiceGrid,
    beforeEnter: () => {
      const token = localStorage.getItem('token')
      const role = roleFromToken(token)
      if (!isAppAdmin(role)) return '/'
    }
  },
  {
    path: '/my-invoices',
    name: 'MyInvoices',
    component: MyInvoices
  },
  {
    path: '/payments',
    name: 'PaymentsList',
    component: PaymentsList
  },
  {
    path: '/payment-return',
    name: 'PaymentReturn',
    component: PaymentReturn
  },
  {
    path: '/reset-password',
    name: 'ResetPassword',
    component: ResetPassword
  },
  {
    path: '/event-log',
    name: 'EventLog',
    component: EventLog
  },
  {
    path: '/energy-prices',
    name: 'EnergyPrices',
    component: EnergyPrices
  },
  {
    path: '/admin/sigenergy-state',
    name: 'AdminSigenergyState',
    component: AdminSigenergyState,
    beforeEnter: () => {
      const token = localStorage.getItem('token')
      const role = roleFromToken(token)
      if (!isAppAdmin(role)) return '/'
    }
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

export default router