import { z } from 'zod'
import type { AssetDetails, SaveAssetBody } from './assetsApi'
import { assetTypes, type AssetStatus, type AssetType } from './labels'

/** The API's limits (Asset.cs); the API checks them again. */
export const assetLimits = {
  assetCode: 50,
  computerName: 64,
  serialNumber: 100,
  description: 1000,
} as const

function noControlCharacters(value: string) {
  return [...value].every((character) => {
    const code = character.charCodeAt(0)
    return code > 0x1f && (code < 0x7f || code > 0x9f)
  })
}

function text(label: string, max: number) {
  return z
    .string()
    .trim()
    .max(max, `${label} en fazla ${max} karakter olabilir.`)
    .refine(noControlCharacters, `${label} geçersiz karakter içeriyor.`)
}

const required = (message: string) => z.string().min(1, message)

/**
 * The add and edit form. Selects hold IDs as text ('' for none), as the inputs do. The brand is not sent: it
 * only narrows the models.
 */
export const assetFormSchema = z.object({
  assetCode: text('Demirbaş kodu', assetLimits.assetCode).pipe(z.string().min(1, 'Demirbaş kodu zorunludur.')),
  assetType: z.enum(assetTypes, { error: 'Demirbaş türü seçilmelidir.' }),
  status: required('Durum seçilmelidir.'),
  brandId: required('Marka seçilmelidir.'),
  modelId: required('Model seçilmelidir.'),
  cityId: required('Şehir seçilmelidir.'),
  locationId: z.string(),
  departmentId: required('Departman seçilmelidir.'),
  computerName: text('Bilgisayar adı', assetLimits.computerName),
  serialNumber: text('Seri numarası', assetLimits.serialNumber),
  description: z.string().trim().max(assetLimits.description, `Açıklama en fazla ${assetLimits.description} karakter olabilir.`),
})

export type AssetFormInput = z.input<typeof assetFormSchema>
export type AssetFormValues = z.output<typeof assetFormSchema>
export type AssetFormField = keyof AssetFormInput

export const emptyAssetForm: AssetFormInput = {
  assetCode: '',
  assetType: '' as AssetType,
  status: 'Available',
  brandId: '',
  modelId: '',
  cityId: '',
  locationId: '',
  departmentId: '',
  computerName: '',
  serialNumber: '',
  description: '',
}

/** The form as the asset is now. */
export function toFormInput(asset: AssetDetails): AssetFormInput {
  return {
    assetCode: asset.assetCode,
    assetType: asset.assetType,
    status: asset.status,
    brandId: String(asset.brand.id),
    modelId: String(asset.model.id),
    cityId: String(asset.city.id),
    locationId: asset.location ? String(asset.location.id) : '',
    departmentId: String(asset.department.id),
    computerName: asset.computerName ?? '',
    serialNumber: asset.serialNumber ?? '',
    description: asset.description ?? '',
  }
}

/** The request body; empty text is sent as null. */
export function toBody(values: AssetFormValues): SaveAssetBody {
  const optional = (value: string) => (value === '' ? null : value)
  return {
    assetCode: values.assetCode,
    assetType: values.assetType,
    status: values.status as AssetStatus,
    modelId: Number(values.modelId),
    cityId: Number(values.cityId),
    departmentId: Number(values.departmentId),
    locationId: values.locationId === '' ? null : Number(values.locationId),
    computerName: optional(values.computerName),
    serialNumber: optional(values.serialNumber),
    description: optional(values.description),
  }
}

/** The API's field names (camelCase) that are fields of this form. */
export const formFields: readonly AssetFormField[] = [
  'assetCode',
  'assetType',
  'status',
  'modelId',
  'cityId',
  'locationId',
  'departmentId',
  'computerName',
  'serialNumber',
  'description',
]
