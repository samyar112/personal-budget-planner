import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { fetchMonthlyCashFlow, formatMoney, type SpendingFilter } from "../../api/transactions";
import ChartCard from "./ChartCard";
import { cssVar, formatCompact, useChartData } from "./chartSupport";

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** Income against expenses per calendar month, with net cash flow as a line. Same rules as the summary cards. */
export default function MonthlyTrendChart({ filter }: { filter: SpendingFilter }) {
  const [state, retry] = useChartData(JSON.stringify(filter), () => fetchMonthlyCashFlow(filter));

  return (
    <ChartCard
      title="Monthly trend"
      subtitle="Income, expenses and net cash flow"
      state={state}
      retry={retry}
      isEmpty={(months) => months.every((month) => month.income === 0 && month.expenses === 0)}
      emptyText="No activity in these months."
    >
      {(months) => {
        const spansYears = months.length > 0 && months[0].year !== months[months.length - 1].year;
        const rows = months.map((month) => ({
          ...month,
          label: spansYears ? `${MONTHS[month.month - 1]} ${String(month.year).slice(2)}` : MONTHS[month.month - 1],
        }));
        return (
          <ResponsiveContainer width="100%" height={300}>
            <ComposedChart data={rows} margin={{ left: 8, right: 16 }}>
              <CartesianGrid vertical={false} strokeOpacity={0.3} />
              <XAxis dataKey="label" />
              <YAxis tickFormatter={formatCompact} width={64} />
              <Tooltip formatter={(value) => formatMoney(Number(value))} />
              <Legend />
              <Bar dataKey="income" name="Income" fill={cssVar("--accent", "#1e6043")} radius={[4, 4, 0, 0]} />
              <Bar dataKey="expenses" name="Expenses" fill={cssVar("--primary", "#e67e22")} radius={[4, 4, 0, 0]} />
              <Line
                dataKey="netCashFlow"
                name="Net cash flow"
                type="monotone"
                stroke={cssVar("--foreground", "#1c2833")}
                strokeWidth={2}
                dot={{ r: 3 }}
              />
            </ComposedChart>
          </ResponsiveContainer>
        );
      }}
    </ChartCard>
  );
}
