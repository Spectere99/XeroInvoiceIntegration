using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Reflection;
using Common.Base;
using MySql.Data.MySqlClient;
using SIMSData.Abstract;

namespace Common.Abstract
{
    public abstract class MySqlObjectReaderBase<T>
    {
        protected abstract MapperBase<T> GetMapper();

        public abstract List<T> GetList();
        public abstract List<T> GetById(int id);

        public abstract List<T> Save(List<T> objList);

        public abstract void Remove(List<T> objList);

        protected string ConnectionString
        {
            get { return ConfigurationManager.ConnectionStrings["DBConnection"].ConnectionString; }
        }

        protected MySqlConnection GetDbConnection()
        {
            return new MySqlConnection(ConnectionString);
        }

        protected MySqlCommand GetDbSqlCommand(string sqlQuery)
        {
            MySqlCommand command = new MySqlCommand
            {
                Connection = GetDbConnection(),
                CommandType = CommandType.Text,
                CommandText = sqlQuery
            };
            return command;
        }

        protected MySqlCommand GetDbStoredProcCommand(string storedProcName)
        {
            MySqlCommand command = new MySqlCommand(storedProcName)
            {
                Connection = GetDbConnection(),
                CommandType = CommandType.StoredProcedure
            };
            return command;
        }

        protected MySqlParameter CreateNullParamter(string name, MySqlDbType paramType)
        {
            MySqlParameter parameter = new MySqlParameter
            {
                MySqlDbType = paramType,
                ParameterName = name,
                Value = null,
                Direction = ParameterDirection.Input
            };
            return parameter;
        }

        protected MySqlParameter CretaeNullParameter(string name, MySqlDbType paramType, int size)
        {
            MySqlParameter parameter = new MySqlParameter
            {
                MySqlDbType = paramType,
                ParameterName = name,
                Size = size,
                Value = null,
                Direction = ParameterDirection.Input
            };
            return parameter;
        }

        protected MySqlParameter CreateOutputParameter(string name, MySqlDbType paramType)
        {
            MySqlParameter parameter = new MySqlParameter
            {
                MySqlDbType = paramType,
                ParameterName = name,
                Direction = ParameterDirection.Output
            };
            return parameter;
        }

        protected MySqlParameter CreateOutputParameter(string name, MySqlDbType paramType, int size)
        {
            MySqlParameter parameter = new MySqlParameter
            {
                MySqlDbType = paramType,
                ParameterName = name,
                Size = size,
                Direction = ParameterDirection.Output
            };
            return parameter;
        }

        protected MySqlParameter CreateParameter(string name, Guid value)
        {
            if (value.Equals(CommonBase.GuidNullValue))
            {
                return CreateNullParamter(name, MySqlDbType.Guid);
            }

            MySqlParameter parameter = new MySqlParameter
            {
                MySqlDbType = MySqlDbType.Guid,
                ParameterName = name,
                Value = value,
                Direction = ParameterDirection.Input
            };
            return parameter;
        }

        protected MySqlParameter CreateParameter(string name, int value)
        {
            if (value == CommonBase.IntNullValue)
            {
                return CreateNullParamter(name, MySqlDbType.Int32);
            }

            MySqlParameter parameter = new MySqlParameter
            {
                MySqlDbType = MySqlDbType.Int32,
                ParameterName = name,
                Value = value,
                Direction = ParameterDirection.Input
            };
            return parameter;
        }

        protected MySqlParameter CreateParameter(string name, DateTime value)
        {
            if (value == CommonBase.DateTimeNullValue)
            {
                return CreateNullParamter(name, MySqlDbType.DateTime);
            }

            MySqlParameter parameter = new MySqlParameter
            {
                MySqlDbType = MySqlDbType.DateTime,
                ParameterName = name,
                Value = value,
                Direction = ParameterDirection.Input
            };
            return parameter;
        }

        protected MySqlParameter CreateParameter(string name, string value, int size)
        {
            if (value == CommonBase.StringNullValue)
            {
                return CreateNullParamter(name, MySqlDbType.VarChar);
            }

            MySqlParameter parameter = new MySqlParameter
            {
                MySqlDbType = MySqlDbType.VarChar,
                ParameterName = name,
                Size = size,
                Value = value,
                Direction = ParameterDirection.Input
            };
            return parameter;
        }

        protected MySqlParameter CreateParameter(string name, bool value)
        {
            MySqlParameter parameter = new MySqlParameter
            {
                MySqlDbType = MySqlDbType.Bit,
                ParameterName = name,
                Value = value,
                Direction = ParameterDirection.Input
            };
            return parameter;
        }

        protected List<T> Execute(MySqlCommand command)
        {
            try
            {
                command.Connection.Open();
                using (IDataReader reader = command.ExecuteReader())
                {
                    try
                    {
                        MapperBase<T> mapper = GetMapper();
                        
                        var resultList = mapper.MapAll(reader);
                        return resultList;
                    }
                    catch (Exception)
                    {

                        throw;
                    }
                    finally
                    {
                        reader.Close();
                    }


                }

            }
            catch (Exception)
            {

                throw;
            }
            finally
            {
                command.Connection.Close();
            }
        }

        protected void ExecuteNoReader(MySqlCommand command)
        {
            try
            {
                command.Connection.Open();
                command.ExecuteNonQuery();
                command.Connection.Close();
            }
            catch (Exception)
            {

                throw;
            }
            finally
            {
                command.Connection.Close();
            }
        }

        protected int ExecuteScalar(MySqlCommand command)
        {
            try
            {
                command.Connection.Open();
                var scalarValue = command.ExecuteScalar();
                command.Connection.Close();

                return (int)scalarValue;

            }
            catch (Exception)
            {

                throw;
            }
            finally
            {
                command.Connection.Close();
            }
        }
    }
}
