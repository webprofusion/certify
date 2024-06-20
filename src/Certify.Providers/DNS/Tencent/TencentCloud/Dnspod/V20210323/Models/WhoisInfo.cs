/*
 * Copyright (c) 2018 THL A29 Limited, a Tencent company. All Rights Reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied.  See the License for the
 * specific language governing permissions and limitations
 * under the License.
 */

namespace TencentCloud.Dnspod.V20210323.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;
    using TencentCloud.Common;

    public class WhoisInfo : AbstractModel
    {
        
        /// <summary>
        /// 联系信息
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("Contacts")]
        public WhoisContact Contacts{ get; set; }

        /// <summary>
        /// 域名注册时间
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("CreationDate")]
        public string CreationDate{ get; set; }

        /// <summary>
        /// 域名到期时间
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("ExpirationDate")]
        public string ExpirationDate{ get; set; }

        /// <summary>
        /// 是否是在腾讯云注册的域名
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("IsQcloud")]
        public bool? IsQcloud{ get; set; }

        /// <summary>
        /// 是否当前操作账号注册的域名
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("IsQcloudOwner")]
        public bool? IsQcloudOwner{ get; set; }

        /// <summary>
        /// 域名配置的NS
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("NameServers")]
        public string[] NameServers{ get; set; }

        /// <summary>
        /// Whois原始信息
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("Raw")]
        public string[] Raw{ get; set; }

        /// <summary>
        /// 域名注册商
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("Registrar")]
        public string[] Registrar{ get; set; }

        /// <summary>
        /// 状态
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("Status")]
        public string[] Status{ get; set; }

        /// <summary>
        /// 更新日期
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("UpdatedDate")]
        public string UpdatedDate{ get; set; }

        /// <summary>
        /// dnssec
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("Dnssec")]
        public string Dnssec{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamObj(map, prefix + "Contacts.", this.Contacts);
            this.SetParamSimple(map, prefix + "CreationDate", this.CreationDate);
            this.SetParamSimple(map, prefix + "ExpirationDate", this.ExpirationDate);
            this.SetParamSimple(map, prefix + "IsQcloud", this.IsQcloud);
            this.SetParamSimple(map, prefix + "IsQcloudOwner", this.IsQcloudOwner);
            this.SetParamArraySimple(map, prefix + "NameServers.", this.NameServers);
            this.SetParamArraySimple(map, prefix + "Raw.", this.Raw);
            this.SetParamArraySimple(map, prefix + "Registrar.", this.Registrar);
            this.SetParamArraySimple(map, prefix + "Status.", this.Status);
            this.SetParamSimple(map, prefix + "UpdatedDate", this.UpdatedDate);
            this.SetParamSimple(map, prefix + "Dnssec", this.Dnssec);
        }
    }
}

