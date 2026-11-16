import { useMutation, useMutationState, useQueryClient } from '@tanstack/react-query'
import type { QueryClient } from '@tanstack/react-query'
import { clearRating, setRating } from '../../api/ratings'
import {
  applyOptimisticRating,
  invalidateRatingQueries,
  removeRatedRecommendation,
  restoreOptimisticRating,
  RECO_DISMISS_TOTAL_MS,
  type UserRating,
} from './ratingCache'

const settlementTimers = new WeakMap<QueryClient, number>()

export function useRatingMutation(titleId: number) {
  const queryClient = useQueryClient()
  const mutationKey = ['rating', titleId] as const
  const states = useMutationState({
    filters: { mutationKey },
    select: (mutation) => ({
      status: mutation.state.status,
      rating: mutation.state.variables as UserRating,
    }),
  })
  const latest = states[states.length - 1]

  const mutation = useMutation({
    mutationKey,
    mutationFn: async (next: UserRating) => {
      if (next == null) await clearRating(titleId)
      else await setRating(titleId, next)
    },
    onMutate: async (next) => {
      await Promise.all([
        queryClient.cancelQueries({ queryKey: ['titles'] }),
        queryClient.cancelQueries({ queryKey: ['my-list'] }),
        queryClient.cancelQueries({ queryKey: ['title'] }),
        queryClient.cancelQueries({ queryKey: ['recommendations'] }),
        queryClient.cancelQueries({ queryKey: ['ratings'] }),
      ])

      const previousTitles = queryClient.getQueriesData({ queryKey: ['titles'] })
      const previousMyList = queryClient.getQueriesData({ queryKey: ['my-list'] })
      const previousTitle = queryClient.getQueriesData({ queryKey: ['title', titleId] })
      const previousReco = queryClient.getQueriesData({ queryKey: ['recommendations'] })
      const previousRatings = queryClient.getQueriesData({ queryKey: ['ratings'] })

      applyOptimisticRating(queryClient, titleId, next)

      return {
        snapshots: [
          ...previousTitles,
          ...previousMyList,
          ...previousTitle,
          ...previousReco,
          ...previousRatings,
        ],
      }
    },
    onError: (_err, _next, context) => {
      if (!context) return
      restoreOptimisticRating(queryClient, titleId, context.snapshots)
    },
    onSettled: (_data, error, next) => {
      if (error == null && next != null) {
        window.setTimeout(() => {
          if (queryClient.isMutating({ mutationKey }) === 0) {
            removeRatedRecommendation(queryClient, titleId)
          }
        }, RECO_DISMISS_TOTAL_MS)
      }
      const previousTimer = settlementTimers.get(queryClient)
      if (previousTimer !== undefined) window.clearTimeout(previousTimer)
      // onSettled still counts as pending. Check after all same-turn settlements dispatch.
      settlementTimers.set(queryClient, window.setTimeout(() => {
        settlementTimers.delete(queryClient)
        if (queryClient.isMutating({ mutationKey: ['rating'] }) !== 0) return
        void invalidateRatingQueries(queryClient, { includeRecommendations: false }).then(() => {
          window.setTimeout(() => {
            if (queryClient.isMutating({ mutationKey: ['rating'] }) === 0) {
              void queryClient.invalidateQueries({ queryKey: ['recommendations'] })
            }
          }, RECO_DISMISS_TOTAL_MS + 50)
        })
      }, 0))
    },
  })

  return {
    ...mutation,
    isPending: mutation.isPending || latest?.status === 'pending',
    isError: latest?.status === 'error',
    pendingRating: latest?.status === 'pending' ? latest.rating : undefined,
    mutate: (next: UserRating) => {
      if (queryClient.isMutating({ mutationKey }) === 0) mutation.mutate(next)
    },
  }
}
