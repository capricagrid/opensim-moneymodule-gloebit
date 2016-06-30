/*
 * Copyright (c) 2015 Gloebit LLC
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Text;
using MySql.Data.MySqlClient;
using Nini.Config;
using OpenSim.Data.MySQL;
using OpenSim.Data.PGSQL;
using OpenSim.Data.SQLite;
using Npgsql;
using NpgsqlTypes;
using OpenMetaverse;  // Necessary for UUID type


namespace Gloebit.GloebitMoneyModule
{
    class GloebitSubscriptionData {

        private static IGloebitSubscriptionData m_impl;

        public static void Initialise(IConfig config) {
            switch(config.GetString("StorageProvider")) {
                case "OpenSim.Data.SQLite.dll":
                    m_impl = new SQLiteImpl(config);
                    break;
                case "OpenSim.Data.MySQL.dll":
                    m_impl = new MySQLImpl(config);
                    break;
                case "OpenSim.Data.PGSQL.dll":
                    m_impl = new PGSQLImpl(config);
                    break;
                default:
                    break;
            }
        }

        public static IGloebitSubscriptionData Instance {
            get { return m_impl; }
        }

        public interface IGloebitSubscriptionData {
            GloebitAPI.Subscription[] Get(string field, string key);

            GloebitAPI.Subscription[] Get(string[] fields, string[] keys);

            bool Store(GloebitAPI.Subscription subscription);

            bool UpdateFromGloebit(GloebitAPI.Subscription subscription);
        }

        private class SQLiteImpl : SQLiteGenericTableHandler<GloebitAPI.Subscription>, IGloebitSubscriptionData {
            public SQLiteImpl(IConfig config)
                : base(config.GetString("ConnectionString"), "GloebitSubscriptions", "GloebitSubscriptionsSQLite")
            {
            }
            /// TODO: Likely need to override Store() function to handle bools, DateTimes and nulls.
            /// Start with SQLiteGenericTableHandler impl and see MySql override below

            public bool UpdateFromGloebit(GloebitAPI.Subscription subscription) {
                // TODO: may need a similar treatment to PGSQL
                return Store(subscription);
            }
            
        }

        private class MySQLImpl : MySQLGenericTableHandler<GloebitAPI.Subscription>, IGloebitSubscriptionData {
            public MySQLImpl(IConfig config)
                : base(config.GetString("ConnectionString"), "GloebitSubscriptions", "GloebitSubscriptionsMySQL")
            {
            }

            public bool UpdateFromGloebit(GloebitAPI.Subscription subscription) {
                // Works because MySql usese Replace Into
                return Store(subscription);
            }
            
            public override bool Store(GloebitAPI.Subscription subscription)
            {
                //            m_log.DebugFormat("[MYSQL GENERIC TABLE HANDLER]: Store(T row) invoked");
                
                using (MySqlCommand cmd = new MySqlCommand())
                {
                    string query = "";
                    List<String> names = new List<String>();
                    List<String> values = new List<String>();
                    
                    foreach (FieldInfo fi in m_Fields.Values)
                    {
                        names.Add(fi.Name);
                        values.Add("?" + fi.Name);
                        
                        // Temporarily return more information about what field is unexpectedly null for
                        // http://opensimulator.org/mantis/view.php?id=5403.  This might be due to a bug in the
                        // InventoryTransferModule or we may be required to substitute a DBNull here.
                        /*if (fi.GetValue(asset) == null)
                            throw new NullReferenceException(
                                                             string.Format(
                                                                           "[MYSQL GENERIC TABLE HANDLER]: Trying to store field {0} for {1} which is unexpectedly null",
                                                                           fi.Name, asset));*/
                        
                        cmd.Parameters.AddWithValue(fi.Name, fi.GetValue(subscription));
                    }
                    
                    /*if (m_DataField != null)
                    {
                        Dictionary<string, string> data =
                        (Dictionary<string, string>)m_DataField.GetValue(row);
                        
                        foreach (KeyValuePair<string, string> kvp in data)
                        {
                            names.Add(kvp.Key);
                            values.Add("?" + kvp.Key);
                            cmd.Parameters.AddWithValue("?" + kvp.Key, kvp.Value);
                        }
                    }*/
                    
                    query = String.Format("replace into {0} (`", m_Realm) + String.Join("`,`", names.ToArray()) + "`) values (" + String.Join(",", values.ToArray()) + ")";
                    
                    cmd.CommandText = query;
                    
                    if (ExecuteNonQuery(cmd) > 0)
                        return true;
                    
                    return false;
                }
            }
        }

        private class PGSQLImpl : PGSQLGenericTableHandler<GloebitAPI.Subscription>, IGloebitSubscriptionData {
            public PGSQLImpl(IConfig config)
                : base(config.GetString("ConnectionString"), "GloebitSubscriptions", "GloebitSubscriptionsPGSQL")
            {
            }
                
            public bool UpdateFromGloebit(GloebitAPI.Subscription subscription) {
                // set Enabled=subscription.Enabled and SubscriptionID=subscription.SubscriptionID)
                //// UPDATE GloebitSubscriptions
                //// SET SubscriptionID=val, Enabled=val
                //// WHERE ObjectID=val AND AppKey=val AND GlbApiUrl=Val
                
                using (NpgsqlConnection conn = new NpgsqlConnection(m_ConnectionString))
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    // Build Query Structure
                    StringBuilder query = new StringBuilder();
                    query.AppendFormat("UPDATE {0} ", m_Realm);
                    query.AppendFormat("SET \"{0}\" = :{0}, \"{1}\" = :{1} ", "SubscriptionID", "Enabled");
                    query.AppendFormat("WHERE \"{0}\" = :{0} AND \"{1}\" = :{1} AND \"{2}\" = :{2}", "ObjectID", "AppKey", "GlbApiUrl");
                    
                    // Add parameters we are going to set or use in where clause
                    string pgFieldType = "";
                    string[] pList = new string[5] {"SubscriptionID", "Enabled", "ObjectID", "AppKey", "GlbApiUrl"};
                    foreach (FieldInfo fi in m_Fields.Values) {
                        // if (pList.Contains(fi.Name)) { --- Can't use Contains before .NET 3.5
                        if (Array.Exists(pList, delegate(string s) { return s.Equals(fi.Name); })) {
                            if (m_FieldTypes.ContainsKey(fi.Name)) {
                                pgFieldType = m_FieldTypes[fi.Name];
                            } else {
                                pgFieldType = "";
                            }
                            cmd.Parameters.Add(createParameter(fi.Name, fi.GetValue(subscription), pgFieldType, true));
                        }
                    }
                    
                    // Execute query
                    cmd.Connection = conn;
                    cmd.CommandText = query.ToString();
                    conn.Open();
                    if (cmd.ExecuteNonQuery() > 0) {
                        //m_log.Info("[PGSQLGenericTable]: UpdateFromGloebit completed successfully");
                        return true;
                    } else {
                        //m_log.Error("[PGSQLGenericTable]: UpdateFromGloebit FAILED!!!!!");
                        return false;
                    }
                    
                }
            }
            
            private NpgsqlParameter createParameter(string pName, object pValue, string pgFieldType, bool input) {
                //HACK if object is null, it is turned into a string, there are no nullable type till now
                if (pValue == null) pValue = "";
                
                NpgsqlDbType dbType = dbtypeFromString(pValue.GetType(), pgFieldType);
                NpgsqlParameter parameter = new NpgsqlParameter(pName, dbType);
                if (input) {
                    parameter.Direction = ParameterDirection.Input;
                    // TODO: convert cpv to use dbType instead of pgFieldType
                    parameter.Value = createParameterValue(pValue, pgFieldType);
                } else {
                    parameter.Direction = ParameterDirection.Output;
                }
                return parameter;
            }
            
            private NpgsqlDbType dbtypeFromString(Type type, string PGFieldType)
            {
                if (PGFieldType == "")
                {
                    return dbtypeFromType(type);
                }
                
                if (PGFieldType == "character varying")
                {
                    return NpgsqlDbType.Varchar;
                }
                if (PGFieldType == "double precision")
                {
                    return NpgsqlDbType.Double;
                }
                if (PGFieldType == "integer")
                {
                    return NpgsqlDbType.Integer;
                }
                if (PGFieldType == "smallint")
                {
                    return NpgsqlDbType.Smallint;
                }
                if (PGFieldType == "boolean")
                {
                    return NpgsqlDbType.Boolean;
                }
                if (PGFieldType == "uuid")
                {
                    return NpgsqlDbType.Uuid;
                }
                if (PGFieldType == "bytea")
                {
                    return NpgsqlDbType.Bytea;
                }
                
                return dbtypeFromType(type);
            }
            
            private NpgsqlDbType dbtypeFromType(Type type)
            {
                if (type == typeof(string))
                {
                    return NpgsqlDbType.Varchar;
                }
                if (type == typeof(double))
                {
                    return NpgsqlDbType.Double;
                }
                if (type == typeof(Single))
                {
                    return NpgsqlDbType.Double;
                }
                if (type == typeof(int))
                {
                    return NpgsqlDbType.Integer;
                }
                if (type == typeof(bool))
                {
                    return NpgsqlDbType.Boolean;
                }
                if (type == typeof(UUID))
                {
                    return NpgsqlDbType.Uuid;
                }
                if (type == typeof(byte))
                {
                    return NpgsqlDbType.Smallint;
                }
                if (type == typeof(sbyte))
                {
                    return NpgsqlDbType.Integer;
                }
                if (type == typeof(Byte[]))
                {
                    return NpgsqlDbType.Bytea;
                }
                if (type == typeof(uint) || type == typeof(ushort))
                {
                    return NpgsqlDbType.Integer;
                }
                if (type == typeof(ulong))
                {
                    return NpgsqlDbType.Bigint;
                }
                if (type == typeof(DateTime))
                {
                    return NpgsqlDbType.Timestamp;
                }
                
                return NpgsqlDbType.Varchar;
            }
            
            private static object createParameterValue(object value)
            {
                Type valueType = value.GetType();
                
                if (valueType == typeof(UUID)) //TODO check if this works
                {
                    return ((UUID) value).Guid;
                }
                if (valueType == typeof(bool))
                {
                    return (bool)value;
                }
                return value;
            }
            
            private static object createParameterValue(object value, string PGFieldType)
            {
                if (PGFieldType == "uuid")
                {
                    UUID uidout;
                    UUID.TryParse(value.ToString(), out uidout);
                    return uidout;
                }
                if (PGFieldType == "integer")
                {
                    int intout;
                    int.TryParse(value.ToString(), out intout);
                    return intout;
                }
                if (PGFieldType == "boolean")
                {
                    return (value.ToString() == "true");
                }
                if (PGFieldType == "timestamp with time zone")
                {
                    return (DateTime)value;
                }
                if (PGFieldType == "timestamp without time zone")
                {
                    return (DateTime)value;
                }
                if (PGFieldType == "double precision")
                {
                    return Convert.ToDouble(value);
                }
                return createParameterValue(value);
            }
            
        }
    }
}
