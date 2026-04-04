import { createRouter, createWebHistory } from 'vue-router'
import Home from '../components/Home.vue'
import UsersList from '../components/UsersList.vue'
import EditUser from '../components/EditUser.vue'
import UploadChargingSessions from '../components/UploadChargingSessions.vue'
import ChargingSessionsList from '../components/ChargingSessionsList.vue'
import ChargingUsersList from '../components/ChargingUsersList.vue'
import EditChargingUser from '../components/EditChargingUser.vue'
import InvoiceGrid from '../components/InvoiceGrid.vue'
import EventLog from '../components/EventLog.vue'
import EnergyPrices from '../components/EnergyPrices.vue'

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
    component: InvoiceGrid
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