export type WorkPostSceneState = 'free' | 'inProgress' | 'overdue' | 'paused' | 'conflict'

export type WorkPostSceneItem = {
  id: string
  name: string
  state: WorkPostSceneState
  vehicleLabel?: string
  registrationPlate?: string | null
  count: number
}
