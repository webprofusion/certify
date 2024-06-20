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

    public class DomainListItem : AbstractModel
    {
        
        /// <summary>
        /// 系统分配给域名的唯一标识
        /// </summary>
        [JsonProperty("DomainId")]
        public ulong? DomainId{ get; set; }

        /// <summary>
        /// 域名的原始格式
        /// </summary>
        [JsonProperty("Name")]
        public string Name{ get; set; }

        /// <summary>
        /// 域名的状态，正常：ENABLE，暂停：PAUSE，封禁：SPAM
        /// </summary>
        [JsonProperty("Status")]
        public string Status{ get; set; }

        /// <summary>
        /// 域名默认的解析记录默认TTL值
        /// </summary>
        [JsonProperty("TTL")]
        public ulong? TTL{ get; set; }

        /// <summary>
        /// 是否开启CNAME加速，开启：ENABLE，未开启：DISABLE
        /// </summary>
        [JsonProperty("CNAMESpeedup")]
        public string CNAMESpeedup{ get; set; }

        /// <summary>
        /// DNS 设置状态，错误：DNSERROR，正常：空字符串
        /// </summary>
        [JsonProperty("DNSStatus")]
        public string DNSStatus{ get; set; }

        /// <summary>
        /// 域名的套餐等级代码
        /// </summary>
        [JsonProperty("Grade")]
        public string Grade{ get; set; }

        /// <summary>
        /// 域名所属的分组Id
        /// </summary>
        [JsonProperty("GroupId")]
        public ulong? GroupId{ get; set; }

        /// <summary>
        /// 是否开启搜索引擎推送优化，是：YES，否：NO
        /// </summary>
        [JsonProperty("SearchEnginePush")]
        public string SearchEnginePush{ get; set; }

        /// <summary>
        /// 域名备注说明
        /// </summary>
        [JsonProperty("Remark")]
        public string Remark{ get; set; }

        /// <summary>
        /// 经过punycode编码后的域名格式
        /// </summary>
        [JsonProperty("Punycode")]
        public string Punycode{ get; set; }

        /// <summary>
        /// 系统为域名分配的有效DNS
        /// </summary>
        [JsonProperty("EffectiveDNS")]
        public string[] EffectiveDNS{ get; set; }

        /// <summary>
        /// 域名套餐等级对应的序号
        /// </summary>
        [JsonProperty("GradeLevel")]
        public ulong? GradeLevel{ get; set; }

        /// <summary>
        /// 套餐名称
        /// </summary>
        [JsonProperty("GradeTitle")]
        public string GradeTitle{ get; set; }

        /// <summary>
        /// 是否是付费套餐
        /// </summary>
        [JsonProperty("IsVip")]
        public string IsVip{ get; set; }

        /// <summary>
        /// 付费套餐开通时间
        /// </summary>
        [JsonProperty("VipStartAt")]
        public string VipStartAt{ get; set; }

        /// <summary>
        /// 付费套餐到期时间
        /// </summary>
        [JsonProperty("VipEndAt")]
        public string VipEndAt{ get; set; }

        /// <summary>
        /// 域名是否开通VIP自动续费，是：YES，否：NO，默认：DEFAULT
        /// </summary>
        [JsonProperty("VipAutoRenew")]
        public string VipAutoRenew{ get; set; }

        /// <summary>
        /// 域名下的记录数量
        /// </summary>
        [JsonProperty("RecordCount")]
        public ulong? RecordCount{ get; set; }

        /// <summary>
        /// 域名添加时间
        /// </summary>
        [JsonProperty("CreatedOn")]
        public string CreatedOn{ get; set; }

        /// <summary>
        /// 域名更新时间
        /// </summary>
        [JsonProperty("UpdatedOn")]
        public string UpdatedOn{ get; set; }

        /// <summary>
        /// 域名所属账号
        /// </summary>
        [JsonProperty("Owner")]
        public string Owner{ get; set; }

        /// <summary>
        /// 域名关联的标签列表
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("TagList")]
        public TagItem[] TagList{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamSimple(map, prefix + "DomainId", this.DomainId);
            this.SetParamSimple(map, prefix + "Name", this.Name);
            this.SetParamSimple(map, prefix + "Status", this.Status);
            this.SetParamSimple(map, prefix + "TTL", this.TTL);
            this.SetParamSimple(map, prefix + "CNAMESpeedup", this.CNAMESpeedup);
            this.SetParamSimple(map, prefix + "DNSStatus", this.DNSStatus);
            this.SetParamSimple(map, prefix + "Grade", this.Grade);
            this.SetParamSimple(map, prefix + "GroupId", this.GroupId);
            this.SetParamSimple(map, prefix + "SearchEnginePush", this.SearchEnginePush);
            this.SetParamSimple(map, prefix + "Remark", this.Remark);
            this.SetParamSimple(map, prefix + "Punycode", this.Punycode);
            this.SetParamArraySimple(map, prefix + "EffectiveDNS.", this.EffectiveDNS);
            this.SetParamSimple(map, prefix + "GradeLevel", this.GradeLevel);
            this.SetParamSimple(map, prefix + "GradeTitle", this.GradeTitle);
            this.SetParamSimple(map, prefix + "IsVip", this.IsVip);
            this.SetParamSimple(map, prefix + "VipStartAt", this.VipStartAt);
            this.SetParamSimple(map, prefix + "VipEndAt", this.VipEndAt);
            this.SetParamSimple(map, prefix + "VipAutoRenew", this.VipAutoRenew);
            this.SetParamSimple(map, prefix + "RecordCount", this.RecordCount);
            this.SetParamSimple(map, prefix + "CreatedOn", this.CreatedOn);
            this.SetParamSimple(map, prefix + "UpdatedOn", this.UpdatedOn);
            this.SetParamSimple(map, prefix + "Owner", this.Owner);
            this.SetParamArrayObj(map, prefix + "TagList.", this.TagList);
        }
    }
}

