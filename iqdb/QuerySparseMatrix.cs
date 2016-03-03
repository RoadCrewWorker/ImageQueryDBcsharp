using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace iqdb
{
    public abstract class QuerySparseMatrix
    {
        protected uint[] row_id_offsets; //maps feature_id into items. 
        protected uint[] column_ids;
        protected float[] values;

        public abstract List<QueryResult> ExecuteQuery(Signature query, float limit_coefficients);
        public abstract void Serialize(BinaryWriter w);
        public abstract void Deserialize(BinaryReader r);
    }
}
