import { defineStore } from 'pinia'
import { ApiError } from '@/api/ApiError'
import { getAdminCurrentUser } from '@/api/adminCurrentUserApi'
import type { AdminCurrentUser } from '@/models/AdminCurrentUser'

type LoadingState = 'unknown' | 'loading' | 'loaded' | 'failed'

export const useAdminCurrentUserStore = defineStore('adminCurrentUser', {
  state: () => ({
    loadingState: 'unknown' as LoadingState,
    currentUser: null as AdminCurrentUser | null,
    errorMessage: null as string | null
  }),
  getters: {
    isSystem: state => state.currentUser?.isSystem === true
  },
  actions: {
    async load(force = false): Promise<AdminCurrentUser | null> {
      if (!force && this.loadingState === 'loaded') {
        return this.currentUser
      }

      this.loadingState = 'loading'
      this.errorMessage = null

      try {
        this.currentUser = await getAdminCurrentUser()
        this.loadingState = 'loaded'
      } catch (error) {
        this.currentUser = null
        this.loadingState = 'failed'
        this.errorMessage = error instanceof ApiError
          ? `Не удалось получить текущего пользователя: HTTP ${error.status}.`
          : 'Не удалось получить текущего пользователя.'
      }

      return this.currentUser
    }
  }
})
