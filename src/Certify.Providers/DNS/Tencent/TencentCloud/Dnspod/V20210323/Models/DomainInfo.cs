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

    public class DomainInfo : AbstractModel
    {
        
        /// <summary>
        /// 域名ID
        /// </summary>
        [JsonProperty("DomainId")]
        public ulong? DomainId{ get; set; }

        /// <summary>
        /// 域名状态
        /// </summary>
        [JsonProperty("Status")]
        public string Status{ get; set; }

        /// <summary>
        /// 域名套餐等级
        /// </summary>
        [JsonProperty("Grade")]
        public string Grade{ get; set; }

        /// <summary>
        /// 域名分组ID
        /// </summary>
        [JsonProperty("GroupId")]
        public ulong? GroupId{ get; set; }

        /// <summary>
        /// 是否星标域名
        /// </summary>
        [JsonProperty("IsMark")]
        public string IsMark{ get; set; }

        /// <summary>
        /// TTL(DNS记录缓存时间)
        /// </summary>
        [JsonProperty("TTL")]
        public ulong? TTL{ get; set; }

        /// <summary>
        /// cname加速启用状态
        /// </summary>
        [JsonProperty("CnameSpeedup")]
        public string CnameSpeedup{ get; set; }

        /// <summary>
        /// 域名备注
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("Remark")]
        public string Remark{ get; set; }

        /// <summary>
        /// 域名Punycode
        /// </summary>
        [JsonProperty("Punycode")]
        public string Punycode{ get; set; }

        /// <summary>
        /// 域名DNS状态
        /// </summary>
        [JsonProperty("DnsStatus")]
        public string DnsStatus{ get; set; }

        /// <summary>
        /// 域名的NS列表
        /// </summary>
        [JsonProperty("DnspodNsList")]
        public string[] DnspodNsList{ get; set; }

        /// <summary>
        /// 域名
        /// </summary>
        [JsonProperty("Domain")]
        public string Domain{ get; set; }

        /// <summary>
        /// 域名等级代号
        /// </summary>
        [JsonProperty("GradeLevel")]
        public ulong? GradeLevel{ get; set; }

        /// <summary>
        /// 域名所属的用户ID
        /// </summary>
        [JsonProperty("UserId")]
        public ulong? UserId{ get; set; }

        /// <summary>
        /// 是否为付费域名
        /// </summary>
        [JsonProperty("IsVip")]
        public string IsVip{ get; set; }

        /// <summary>
        /// 域名所有者的账号
        /// </summary>
        [JsonProperty("Owner")]
        public string Owner{ get; set; }

        /// <summary>
        /// 域名等级的描述
        /// </summary>
        [JsonProperty("GradeTitle")]
        public string GradeTitle{ get; set; }

        /// <summary>
        /// 域名创建时间
        /// </summary>
        [JsonProperty("CreatedOn")]
        public string CreatedOn{ get; set; }

        /// <summary>
        /// 最后操作时间
        /// </summary>
        [JsonProperty("UpdatedOn")]
        public string UpdatedOn{ get; set; }

        /// <summary>
        /// 腾讯云账户Uin
        /// </summary>
        [JsonProperty("Uin")]
        public string Uin{ get; set; }

        /// <summary>
        /// 域名实际使用的NS列表
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("ActualNsList")]
        public string[] ActualNsList{ get; set; }

        /// <summary>
        /// 域名的记录数量
        /// </summary>
        [JsonProperty("RecordCount")]
        public ulong? RecordCount{ get; set; }

        /// <summary>
        /// 域名所有者的账户昵称
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("OwnerNick")]
        public string OwnerNick{ get; set; }

        /// <summary>
        /// 是否在付费套餐宽限期
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("IsGracePeriod")]
        public string IsGracePeriod{ get; set; }

        /// <summary>
        /// 是否在付费套餐缓冲期
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("VipBuffered")]
        public string VipBuffered{ get; set; }

        /// <summary>
        /// VIP套餐有效期开始时间
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("VipStartAt")]
        public string VipStartAt{ get; set; }

        /// <summary>
        /// VIP套餐有效期结束时间
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("VipEndAt")]
        public string VipEndAt{ get; set; }

        /// <summary>
        /// VIP套餐自动续费标识。可能的值为：default-默认；no-不自动续费；yes-自动续费
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("VipAutoRenew")]
        public string VipAutoRenew{ get; set; }

        /// <summary>
        /// VIP套餐资源ID
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("VipResourceId")]
        public string VipResourceId{ get; set; }

        /// <summary>
        /// 是否是子域名。
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("IsSubDomain")]
        public bool? IsSubDomain{ get; set; }

        /// <summary>
        /// 域名关联的标签列表
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("TagList")]
        public TagItem[] TagList{ get; set; }

        /// <summary>
        /// 是否启用搜索引擎推送
        /// </summary>
        [JsonProperty("SearchEnginePush")]
        public string SearchEnginePush{ get; set; }

        /// <summary>
        /// 是否开启辅助 DNS
        /// </summary>
        [JsonProperty("SlaveDNS")]
        public string SlaveDNS{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamSimple(map, prefix + "DomainId", this.DomainId);
            this.SetParamSimple(map, prefix + "Status", this.Status);
            this.SetParamSimple(map, prefix + "Grade", this.Grade);
            this.SetParamSimple(map, prefix + "GroupId", this.GroupId);
            this.SetParamSimple(map, prefix + "IsMark", this.IsMark);
            this.SetParamSimple(map, prefix + "TTL", this.TTL);
            this.SetParamSimple(map, prefix + "CnameSpeedup", this.CnameSpeedup);
            this.SetParamSimple(map, prefix + "Remark", this.Remark);
            this.SetParamSimple(map, prefix + "Punycode", this.Punycode);
            this.SetParamSimple(map, prefix + "DnsStatus", this.DnsStatus);
            this.SetParamArraySimple(map, prefix + "DnspodNsList.", this.DnspodNsList);
            this.SetParamSimple(map, prefix + "Domain", this.Domain);
            this.SetParamSimple(map, prefix + "GradeLevel", this.GradeLevel);
            this.SetParamSimple(map, prefix + "UserId", this.UserId);
            this.SetParamSimple(map, prefix + "IsVip", this.IsVip);
            this.SetParamSimple(map, prefix + "Owner", this.Owner);
            this.SetParamSimple(map, prefix + "GradeTitle", this.GradeTitle);
            this.SetParamSimple(map, prefix + "CreatedOn", this.CreatedOn);
            this.SetParamSimple(map, prefix + "UpdatedOn", this.UpdatedOn);
            this.SetParamSimple(map, prefix + "Uin", this.Uin);
            this.SetParamArraySimple(map, prefix + "ActualNsList.", this.ActualNsList);
            this.SetParamSimple(map, prefix + "RecordCount", this.RecordCount);
            this.SetParamSimple(map, prefix + "OwnerNick", this.OwnerNick);
            this.SetParamSimple(map, prefix + "IsGracePeriod", this.IsGracePeriod);
            this.SetParamSimple(map, prefix + "VipBuffered", this.VipBuffered);
            this.SetParamSimple(map, prefix + "VipStartAt", this.VipStartAt);
            this.SetParamSimple(map, prefix + "VipEndAt", this.VipEndAt);
            this.SetParamSimple(map, prefix + "VipAutoRenew", this.VipAutoRenew);
            this.SetParamSimple(map, prefix + "VipResourceId", this.VipResourceId);
            this.SetParamSimple(map, prefix + "IsSubDomain", this.IsSubDomain);
            this.SetParamArrayObj(map, prefix + "TagList.", this.TagList);
            this.SetParamSimple(map, prefix + "SearchEnginePush", this.SearchEnginePush);
            this.SetParamSimple(map, prefix + "SlaveDNS", this.SlaveDNS);
        }
    }
}

