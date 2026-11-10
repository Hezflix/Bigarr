import { Bar, BarChart, CartesianGrid, XAxis, YAxis } from 'recharts'
import { ChartContainer, ChartLegend, ChartLegendContent, ChartTooltip, ChartTooltipContent, type ChartConfig } from '@/components/ui/chart'

export type ChartRow = Record<string, string | number>

/** The series colours: films and episodes in the theme's greys, decisions in the colours of their badges. */
export const seriesColors = {
  movies: 'var(--chart-3)',
  episodes: 'var(--chart-1)',
  plays: 'var(--chart-3)',
  directPlay: 'rgb(16 185 129)',
  directStream: 'rgb(59 130 246)',
  transcode: 'rgb(245 158 11)',
} as const

export type SeriesKey = keyof typeof seriesColors

/**
 * Stacked bars over a `label` column, one series per key, with the shadcn tooltip and, for several series, a legend.
 * Recharts is only bundled with the stats page, which is loaded on demand.
 */
export function StackedBars({ rows, series, labels, height = 200 }: { rows: ChartRow[]; series: SeriesKey[]; labels: Record<SeriesKey, string>; height?: number }) {
  const config = Object.fromEntries(series.map((key) => [key, { label: labels[key], color: seriesColors[key] }])) satisfies ChartConfig
  return (
    <ChartContainer config={config} className="aspect-auto w-full" style={{ height }}>
      <BarChart data={rows} margin={{ left: -16, right: 4, top: 4 }} accessibilityLayer>
        <CartesianGrid vertical={false} strokeDasharray="4 4" />
        <XAxis dataKey="label" tickLine={false} axisLine={false} tickMargin={6} minTickGap={16} fontSize={11} />
        <YAxis tickLine={false} axisLine={false} width={44} fontSize={11} allowDecimals={false} />
        <ChartTooltip cursor={{ fill: 'var(--muted)' }} content={<ChartTooltipContent />} />
        {series.length > 1 && <ChartLegend content={<ChartLegendContent />} />}
        {series.map((key, i) => (
          <Bar key={key} dataKey={key} stackId="stack" fill={`var(--color-${key})`} radius={i === series.length - 1 ? [3, 3, 0, 0] : 0} isAnimationActive={false} />
        ))}
      </BarChart>
    </ChartContainer>
  )
}
