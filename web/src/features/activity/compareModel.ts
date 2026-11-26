/**
 * Of the releases the followed application grabbed, how many Bigarr would have grabbed too. `otherOnly` is what the
 * application grabbed alone; what Bigarr picked alone is not one of its grabs. No percentage when it grabbed nothing.
 */
export function compareAgreement(counts: { same: number; different: number }, otherOnly: number): { grabs: number; percent: number | null } {
  const grabs = counts.same + counts.different + otherOnly
  return { grabs, percent: grabs > 0 ? Math.round((counts.same / grabs) * 100) : null }
}
