type MemberSelectSource = {
  value: string
  employeeName?: string | null
  email?: string | null
  userName?: string | null
}

export function createMemberSelectOptions(members: readonly MemberSelectSource[]) {
  const names = members.map(member => member.employeeName?.trim().replace(/\s+/g, ' ') ?? '')
  const nameCounts = new Map<string, number>()

  for (const name of names) {
    if (!name) continue
    const key = name.toLocaleLowerCase('ru')
    nameCounts.set(key, (nameCounts.get(key) ?? 0) + 1)
  }

  return members.map((member, index) => {
    const name = names[index]!
    const email = member.email?.trim()
    const fallback = email || member.userName?.trim() || member.value
    const hasDuplicateName = name && (nameCounts.get(name.toLocaleLowerCase('ru')) ?? 0) > 1

    return {
      value: member.value,
      label: !name ? fallback : hasDuplicateName ? `${name} (${fallback})` : name
    }
  })
}
