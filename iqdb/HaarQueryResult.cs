using System;

namespace iqdb
{
    public class HaarQueryResult : QueryResult
    {
        public float YuvDistance, MetricValue, MetricMax;
        public override float Score() { return (MetricValue / MetricMax) * 0.8f + (1f - 0.8f) * (1.0f - YuvDistance); }

        public HaarQueryResult(string hash, float yuv_distance, float metric_value, float metric_max)
        {
            this.Hash = hash;
            this.YuvDistance = yuv_distance;
            this.MetricValue = metric_value;
            this.MetricMax = metric_max;
        }

        public override string ToCSV()
        {
            return this.Query.HashString + "\t" + this.Hash + "\t" + this.Score() + "\t" + this.YuvDistance + "\t" + this.MetricValue + "\t" + this.MetricMax;
        }

        public override string ToJSON()
        {
            return "{\"omd5\":\"" + (this.Query.Hash==null?"None":this.Query.HashString) + "\",\"md5\":\"" + this.Hash + "\",\"score\":" + this.Score() + ",\"e_avg\":" + this.YuvDistance + ",\"w_m\":" + this.MetricValue + ",\"w_t\":" + this.MetricMax + "}";
        }
    }
}
