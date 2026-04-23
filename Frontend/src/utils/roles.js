/** MS JWT role claim (same as .NET `ClaimTypes.Role`) */
export const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'

export function isAppAdmin (role) {
  return role === 'Admin' || role === 'Owner'
}

/** List users and edit profiles (roles: Owner still owner/admin-self rules on server). */
export function canManageUserProfiles (role) {
  return role === 'Base' || isAppAdmin(role)
}

export function isOwner (role) {
  return role === 'Owner'
}

export function roleFromToken (token) {
  if (!token) return null
  try {
    const payload = JSON.parse(atob(token.split('.')[1]))
    return payload[ROLE_CLAIM] ?? null
  } catch {
    return null
  }
}
