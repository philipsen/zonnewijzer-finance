import { createRouter, createWebHistory } from 'vue-router'
import Home from '../components/Home.vue'
import UsersList from '../components/UsersList.vue'
import EditUser from '../components/EditUser.vue'
import UploadChargingSessions from '../components/UploadChargingSessions.vue'
import ChargingSessionsList from '../components/ChargingSessionsList.vue'
import MyChargingSessions from '../components/MyChargingSessions.vue'
import ChargingUsersList from '../components/ChargingUsersList.vue'
import EditChargingUser from '../components/EditChargingUser.vue'
import InvoiceGrid from '../components/InvoiceGrid.vue'
import MyInvoices from '../components/MyInvoices.vue'
import PaymentsList from '../components/PaymentsList.vue'
import PaymentReturn from '../components/PaymentReturn.vue'
import EventLog from '../components/EventLog.vue'
import EnergyPrices from '../components/EnergyPrices.vue'
import ChargingStationsPage from '../components/ChargingStationsPage.vue'

const routes = [
  {
    path: '/',
    name: 'Home',
    component: Home
  },
  {
    path: '/users',
    name: 'Users',
    component: UsersList
  },
  {
    path: '/users/:id/edit',
    name: 'EditUser',
    component: EditUser
  },
  {
    path: '/upload-sessions',
    name: 'UploadChargingSessions',
    component: UploadChargingSessions
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
      try {
        const token = localStorage.getItem('token')
        if (!token) return '/'
        const payload = JSON.parse(atob(token.split('.')[1]))
        const role = payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
        if (role !== 'Admin') return '/'
      } catch {
        return '/'
      }
    }
  },
  {
    path: '/my-sessions',
    name: 'MyChargingSessions',
    component: MyChargingSessions
  },
  {
    path: '/charging-users',
    name: 'ChargingUsersList',
    component: ChargingUsersList
  },
  {
    path: '/charging-users/:id/edit',
    name: 'EditChargingUser',
    component: EditChargingUser
  },
  {
    path: '/invoices',
    name: 'InvoiceGrid',
    component: InvoiceGrid,
    beforeEnter: () => {
      try {
        const token = localStorage.getItem('token')
        if (!token) return '/'
        const payload = JSON.parse(atob(token.split('.')[1]))
        const role = payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
        if (role !== 'Admin') return '/'
      } catch {
        return '/'
      }
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
    path: '/event-log',
    name: 'EventLog',
    component: EventLog
  },
  {
    path: '/energy-prices',
    name: 'EnergyPrices',
    component: EnergyPrices
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

export default router