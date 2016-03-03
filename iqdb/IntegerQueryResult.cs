using System;

namespace iqdb
{
    public class IntegerQueryResult : QueryResult
    {
        public float MetricValue, MetricMax;
        public override float Score() { return this.MetricValue / this.MetricMax; }

        public IntegerQueryResult(string hash, float metric_value, float metric_max)
        {
            this.Hash = hash;
            this.MetricValue = metric_value;
            this.MetricMax = metric_max;
        }

        public override string ToCSV()
        {
            return this.Hash + "\t" + this.Score() + "\t" + this.MetricValue + "\t" + this.MetricMax;
        }

        public override string ToJSON()
        {
            return "{\"md5\":\"" + this.Hash + "\",\"score\":" + this.Score() + ",\"w_m\":" + this.MetricValue + ",\"w_t\":" + this.MetricMax + "}";
        }
    }
}
