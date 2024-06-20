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

    public class PackageDetailItem : AbstractModel
    {
        
        /// <summary>
        /// 套餐原价
        /// </summary>
        [JsonProperty("RealPrice")]
        public ulong? RealPrice{ get; set; }

        /// <summary>
        /// 可更换域名次数
        /// </summary>
        [JsonProperty("ChangedTimes")]
        public ulong? ChangedTimes{ get; set; }

        /// <summary>
        /// 允许设置的最小 TTL 值
        /// </summary>
        [JsonProperty("MinTtl")]
        public ulong? MinTtl{ get; set; }

        /// <summary>
        /// 负载均衡数量
        /// </summary>
        [JsonProperty("RecordRoll")]
        public ulong? RecordRoll{ get; set; }

        /// <summary>
        /// 子域名级数
        /// </summary>
        [JsonProperty("SubDomainLevel")]
        public ulong? SubDomainLevel{ get; set; }

        /// <summary>
        /// 泛解析级数
        /// </summary>
        [JsonProperty("MaxWildcard")]
        public ulong? MaxWildcard{ get; set; }

        /// <summary>
        /// DNS 服务集群个数
        /// </summary>
        [JsonProperty("DnsServerRegion")]
        public string DnsServerRegion{ get; set; }

        /// <summary>
        /// 套餐名称
        /// </summary>
        [JsonProperty("DomainGradeCn")]
        public string DomainGradeCn{ get; set; }

        /// <summary>
        /// 套餐代号
        /// </summary>
        [JsonProperty("GradeLevel")]
        public ulong? GradeLevel{ get; set; }

        /// <summary>
        /// 套餐对应的 NS
        /// </summary>
        [JsonProperty("Ns")]
        public string[] Ns{ get; set; }

        /// <summary>
        /// 套餐代码
        /// </summary>
        [JsonProperty("DomainGrade")]
        public string DomainGrade{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamSimple(map, prefix + "RealPrice", this.RealPrice);
            this.SetParamSimple(map, prefix + "ChangedTimes", this.ChangedTimes);
            this.SetParamSimple(map, prefix + "MinTtl", this.MinTtl);
            this.SetParamSimple(map, prefix + "RecordRoll", this.RecordRoll);
            this.SetParamSimple(map, prefix + "SubDomainLevel", this.SubDomainLevel);
            this.SetParamSimple(map, prefix + "MaxWildcard", this.MaxWildcard);
            this.SetParamSimple(map, prefix + "DnsServerRegion", this.DnsServerRegion);
            this.SetParamSimple(map, prefix + "DomainGradeCn", this.DomainGradeCn);
            this.SetParamSimple(map, prefix + "GradeLevel", this.GradeLevel);
            this.SetParamArraySimple(map, prefix + "Ns.", this.Ns);
            this.SetParamSimple(map, prefix + "DomainGrade", this.DomainGrade);
        }
    }
}

