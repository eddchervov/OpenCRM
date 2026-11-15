import { defineStore } from 'pinia'
import { ApiError } from '@/api/ApiError'
import { getAdminPermissions } from '@/api/adminPermissionsApi'
import type { AdminPermissions } from '@/models/AdminPermissions'

type AccessState = 'unknown' | 'loading' | 'allowed' | 'denied' | 'failed'

export const useSystemAccessStore = defineStore('systemAccess', {
  state: () => ({
    accessState: 'unknown' as AccessState,
    permissions: null as AdminPermissions | null,
    errorMessage: null as string | null
  }),
  getters: {
    isSystem: state => state.accessState === 'allowed'
  },
  actions: {
    async load(force = false): Promise<boolean> {
      if (!force && this.accessState === 'allowed') {
        return true
      }

      if (!force && this.accessState === 'denied') {
        return false
      }

      this.accessState = 'loading'
      this.errorMessage = null

      try {
        this.permissions = await getAdminPermissions()
        this.accessState = 'allowed'

        return true
      } catch (error) {
        this.permissions = null
        if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
          this.accessState = 'denied'
          return false
        }

        this.accessState = 'failed'
        this.errorMessage = 'Не удалось получить настройки административных ролей.'

        return false
      }
    }
  }
})
