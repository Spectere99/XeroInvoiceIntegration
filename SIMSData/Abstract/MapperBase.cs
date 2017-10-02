using System;
using System.Collections.Generic;
using System.Data;

namespace SIMSData.Abstract
{
    public abstract class MapperBase<T>
    {
        protected abstract T Map(IDataRecord record);
        protected abstract void BindOrdinals(IDataReader reader);

        public List<T> MapAll(IDataReader reader)
        {
            List<T> list = new List<T>();

            BindOrdinals(reader);
            while (reader.Read())
            {
                try
                {
                    list.Add(Map(reader));
                }
                catch (Exception)
                {
                    
                    throw;
                }
            }

            return list;
        }
    }
}
