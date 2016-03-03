
using System;
namespace iqdb
{
    public abstract class QueryResult : IComparable<QueryResult>
    {
        public Signature Query;
        public string Hash;
        public string HashString() { return Hash; }

        public abstract string ToJSON();
        public abstract string ToCSV();
        public abstract float Score();
        public int CompareTo(QueryResult other)
        {
            return -this.Score().CompareTo(other.Score());
        }
    }
}
