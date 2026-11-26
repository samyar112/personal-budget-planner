import { Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { categoryLabel, fetchSpendingByCategory, formatMoney, type SpendingFilter } from "../../api/transactions";
import ChartCard from "./ChartCard";
import { cssVar, formatCompact, useChartData } from "./chartSupport";

type Row = { category: string; label: string; amount: number; share: number };

/**
 * Spending by category, largest first. Clicking a bar selects that category (click again to clear);
 * the parent uses it to narrow the merchants chart. Categories netted to zero or below by refunds are
 * not drawn, but still count in the total.
 */
export default function CategoryChart({
  filter,
  selected,
  onSelect,
}: {
  filter: SpendingFilter;
  selected: string | null;
  onSelect: (category: string | null) => void;
}) {
  const [state, retry] = useChartData(JSON.stringify(filter), () => fetchSpendingByCategory(filter));
  const primary = cssVar("--primary", "#e67e22");
  const muted = cssVar("--primary-soft", "rgba(230, 126, 34, 0.2)");

  return (
    <ChartCard
      title="Spending by category"
      subtitle={state.status === "ready" ? `Total ${formatMoney(state.data.total)}` : undefined}
      state={state}
      retry={retry}
      isEmpty={(data) => !data.categories.some((category) => category.amount > 0)}
      emptyText="No spending in this period."
    >
      {(data) => {
        const rows: Row[] = data.categories
          .filter((category) => category.amount > 0)
          .map((category) => ({
            category: category.category,
            label: categoryLabel(category.category),
            amount: category.amount,
            share: data.total > 0 ? category.amount / data.total : 0,
          }));
        return (
          <ResponsiveContainer width="100%" height={rows.length * 38 + 40}>
            <BarChart data={rows} layout="vertical" margin={{ left: 8, right: 24 }}>
              <CartesianGrid horizontal={false} strokeOpacity={0.3} />
              <XAxis type="number" tickFormatter={formatCompact} />
              <YAxis type="category" dataKey="label" width={170} tick={{ fontSize: 13 }} />
              <Tooltip
                cursor={{ fillOpacity: 0.1 }}
                formatter={(value, _name, item) => [
                  `${formatMoney(Number(value))} · ${Math.round(((item.payload as Row).share) * 100)}%`,
                  "Spent",
                ]}
              />
              <Bar
                dataKey="amount"
                radius={[0, 6, 6, 0]}
                cursor="pointer"
                onClick={(entry) => {
                  const category = (entry.payload as Row).category;
                  onSelect(category === selected ? null : category);
                }}
              >
                {rows.map((row) => (
                  <Cell key={row.category} fill={selected && selected !== row.category ? muted : primary} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        );
      }}
    </ChartCard>
  );
}
