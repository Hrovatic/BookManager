import axios from 'axios'
import { apiUrl } from '@/utils/api'

const CENTER_API = apiUrl('/api/center')

export async function fetchCenters() {
  const res = await axios.get(CENTER_API)
  const data = Array.isArray(res?.data) ? res.data : []

  return data
    .map((item) => {
      const id = Number(item?.id ?? item?.Id)
      const name = String(item?.name ?? item?.Name ?? '').trim()
      return { id, name }
    })
    .filter((item) => Number.isInteger(item.id) && item.id > 0 && item.name)
    .sort((a, b) => a.id - b.id)
}
