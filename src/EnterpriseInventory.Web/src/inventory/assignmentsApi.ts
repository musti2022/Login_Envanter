import { apiFetch } from '../api/http'
import { assetsQueryKey, type AssetDetails, type PagedResult } from './assetsApi'

/** A row of GET /api/employees/search: an enabled Active Directory account that can be given an asset. */
export interface EmployeeSearchItem {
  objectGuid: string
  userName: string
  displayName: string
  email: string | null
  department: string | null
  title: string | null
}

export interface EmployeeSearchResponse {
  items: EmployeeSearchItem[]
  /** More people match than the directory returned; the user should type more. */
  hasMore: boolean
}

/** The API's search rules (EmployeeService.cs); it checks them again. */
export const employeeSearchLimits = { minLength: 2, maxLength: 64 } as const

export const employeeSearchQueryKey = (term: string) => ['employees', 'search', term] as const

export function searchEmployees(term: string, signal?: AbortSignal) {
  return apiFetch<EmployeeSearchResponse>(`/api/employees/search?q=${encodeURIComponent(term)}`, { signal })
}

/** One assignment period of GET /api/assets/{id}/assignments; returnedAt is null while the asset is held. */
export interface AssetAssignmentItem {
  id: number
  employeeId: number
  userName: string
  displayName: string
  department: string | null
  assignmentDescription: string | null
  notes: string | null
  assignedAt: string
  assignedBy: string
  returnedAt: string | null
  returnedBy: string | null
}

export const assignmentsPageSize = 10

export const assetAssignmentsQueryKey = (id: number, page: number) => [...assetsQueryKey, 'assignments', id, page] as const

export function fetchAssetAssignments(id: number, page: number, signal?: AbortSignal) {
  return apiFetch<PagedResult<AssetAssignmentItem>>(`/api/assets/${id}/assignments?page=${page}&pageSize=${assignmentsPageSize}`, { signal })
}

/** Limits of the assignment texts (AssetAssignment.cs); the API checks them again. */
export const assignmentLimits = { assignmentDescription: 500, notes: 1000 } as const

export interface AssignAssetBody {
  employeeObjectGuid: string
  assignmentDescription: string
  notes: string | null
}

/** Gives the asset to the employee; refused with 409 when someone changed the asset since rowVersion was read. */
export function assignAsset(id: number, body: AssignAssetBody, rowVersion: string) {
  return apiFetch<AssetDetails>(`/api/assets/${id}/assignments`, { method: 'POST', body: { ...body, rowVersion } })
}

/** Takes the asset back from its holder; the assignment stays in the history. */
export function returnAsset(id: number, rowVersion: string) {
  return apiFetch<AssetDetails>(`/api/assets/${id}/returns`, { method: 'POST', body: { rowVersion } })
}
