import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { categoryLabel, fetchTopMerchants, formatMoney, type MerchantSpending, type SpendingFilter } from "../../api/transactions";
import ChartCard from "./ChartCard";
import { cssVar, formatCompact, useChartData } from "./chartSupport";

const LIMIT = 10;

/** The ten largest payees in the period, net of refunds, optionally within one category. */
export default function TopMerchantsChart({
  filter,
  category,
  onClearCategory,
}: {
  filter: SpendingFilter;
  category: string | null;
  onClearCategory: () => void;
}) {
  const query = { ...filter, category: category ?? undefined, limit: LIMIT };
  const [state, retry] = useChartData(JSON.stringify(query), () => fetchTopMerchants(query));

  const subtitle = category ? (
    <>
      {categoryLabel(category)}
      <button type="button" className="btn btn-link btn-sm p-0 ms-2 align-baseline" onClick={onClearCategory}>
        Show all
      </button>
    </>
  ) : (
    "All categories · click a category bar to narrow"
  );

  return (
    <ChartCard
      title="Top merchants"
      subtitle={subtitle}
      state={state}
      retry={retry}
      isEmpty={(merchants) => merchants.length === 0}
      emptyText="No merchants in this period."
    >
      {(merchants) => (
        <ResponsiveContainer width="100%" height={merchants.length * 38 + 40}>
          <BarChart data={merchants} layout="vertical" margin={{ left: 8, right: 24 }}>
            <CartesianGrid horizontal={false} strokeOpacity={0.3} />
            <XAxis type="number" tickFormatter={formatCompact} />
            <YAxis type="category" dataKey="merchant" width={170} tick={{ fontSize: 13 }} />
            <Tooltip
              cursor={{ fillOpacity: 0.1 }}
              formatter={(value, _name, item) => {
                const count = (item.payload as MerchantSpending).count;
                return [`${formatMoney(Number(value))} · ${count} ${count === 1 ? "payment" : "payments"}`, "Spent"];
              }}
            />
            <Bar dataKey="amount" fill={cssVar("--accent", "#1e6043")} radius={[0, 6, 6, 0]} />
          </BarChart>
        </ResponsiveContainer>
      )}
    </ChartCard>
  );
}
