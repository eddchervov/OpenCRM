export type ShiftAvailability = { isAvailable: boolean | null }

export async function checkShiftAvailability(
  organizationId: string,
  employeeId: string,
  startLocal: string,
  endLocal: string
): Promise<ShiftAvailability> {
  const parameters = new URLSearchParams({ employeeId, startLocal, endLocal })
  const response = await fetch(`/api/v1/organizations/${organizationId}/shifts/availability?${parameters}`, {
    headers: { Accept: 'application/json' }
  })
  if (response.status === 403) return { isAvailable: null }
  if (!response.ok) throw new Error('Не удалось проверить график.')
  return response.json() as Promise<ShiftAvailability>
}
