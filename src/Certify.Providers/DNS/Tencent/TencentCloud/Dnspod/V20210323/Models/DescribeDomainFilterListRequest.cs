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

    public class DescribeDomainFilterListRequest : AbstractModel
    {
        
        /// <summary>
        /// 根据域名分组类型获取域名。可取值为 ALL，MINE，SHARE，RECENT。
        /// ALL：全部
        /// MINE：我的域名
        /// SHARE：共享给我的域名
        /// RECENT：最近操作过的域名
        /// </summary>
        [JsonProperty("Type")]
        public string Type{ get; set; }

        /// <summary>
        /// 记录开始的偏移, 第一条记录为 0, 依次类推。默认值为 0。
        /// </summary>
        [JsonProperty("Offset")]
        public ulong? Offset{ get; set; }

        /// <summary>
        /// 要获取的域名数量, 比如获取 20 个, 则为 20。默认值为 5000。如果账户中的域名数量超过了 5000, 将会强制分页并且只返回前 5000 条, 这时需要通过 Offset 和 Limit 参数去获取其它域名。
        /// </summary>
        [JsonProperty("Limit")]
        public ulong? Limit{ get; set; }

        /// <summary>
        /// 根据域名分组 id 获取域名，可通过 DescribeDomain 或 DescribeDomainList 接口 GroupId 字段获取。
        /// </summary>
        [JsonProperty("GroupId")]
        public long?[] GroupId{ get; set; }

        /// <summary>
        /// 根据关键字获取域名。
        /// </summary>
        [JsonProperty("Keyword")]
        public string Keyword{ get; set; }

        /// <summary>
        /// 排序字段。可取值为 NAME，STATUS，RECORDS，GRADE，UPDATED_ON。
        /// NAME：域名名称
        /// STATUS：域名状态
        /// RECORDS：记录数量
        /// GRADE：套餐等级
        /// UPDATED_ON：更新时间
        /// </summary>
        [JsonProperty("SortField")]
        public string SortField{ get; set; }

        /// <summary>
        /// 排序类型，升序：ASC，降序：DESC。
        /// </summary>
        [JsonProperty("SortType")]
        public string SortType{ get; set; }

        /// <summary>
        /// 根据域名状态获取域名。可取值为 ENABLE，LOCK，PAUSE，SPAM。
        /// ENABLE：正常
        /// LOCK：锁定
        /// PAUSE：暂停
        /// SPAM：封禁
        /// </summary>
        [JsonProperty("Status")]
        public string[] Status{ get; set; }

        /// <summary>
        /// 根据套餐获取域名，可通过 DescribeDomain 或 DescribeDomainList 接口 Grade 字段获取。
        /// </summary>
        [JsonProperty("Package")]
        public string[] Package{ get; set; }

        /// <summary>
        /// 根据备注信息获取域名。
        /// </summary>
        [JsonProperty("Remark")]
        public string Remark{ get; set; }

        /// <summary>
        /// 要获取域名的更新时间起始时间点，如 '2021-05-01 03:00:00'。
        /// </summary>
        [JsonProperty("UpdatedAtBegin")]
        public string UpdatedAtBegin{ get; set; }

        /// <summary>
        /// 要获取域名的更新时间终止时间点，如 '2021-05-10 20:00:00'。
        /// </summary>
        [JsonProperty("UpdatedAtEnd")]
        public string UpdatedAtEnd{ get; set; }

        /// <summary>
        /// 要获取域名的记录数查询区间起点。
        /// </summary>
        [JsonProperty("RecordCountBegin")]
        public ulong? RecordCountBegin{ get; set; }

        /// <summary>
        /// 要获取域名的记录数查询区间终点。
        /// </summary>
        [JsonProperty("RecordCountEnd")]
        public ulong? RecordCountEnd{ get; set; }

        /// <summary>
        /// 项目ID
        /// </summary>
        [JsonProperty("ProjectId")]
        public long? ProjectId{ get; set; }

        /// <summary>
        /// 标签过滤
        /// </summary>
        [JsonProperty("Tags")]
        public TagItemFilter[] Tags{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamSimple(map, prefix + "Type", this.Type);
            this.SetParamSimple(map, prefix + "Offset", this.Offset);
            this.SetParamSimple(map, prefix + "Limit", this.Limit);
            this.SetParamArraySimple(map, prefix + "GroupId.", this.GroupId);
            this.SetParamSimple(map, prefix + "Keyword", this.Keyword);
            this.SetParamSimple(map, prefix + "SortField", this.SortField);
            this.SetParamSimple(map, prefix + "SortType", this.SortType);
            this.SetParamArraySimple(map, prefix + "Status.", this.Status);
            this.SetParamArraySimple(map, prefix + "Package.", this.Package);
            this.SetParamSimple(map, prefix + "Remark", this.Remark);
            this.SetParamSimple(map, prefix + "UpdatedAtBegin", this.UpdatedAtBegin);
            this.SetParamSimple(map, prefix + "UpdatedAtEnd", this.UpdatedAtEnd);
            this.SetParamSimple(map, prefix + "RecordCountBegin", this.RecordCountBegin);
            this.SetParamSimple(map, prefix + "RecordCountEnd", this.RecordCountEnd);
            this.SetParamSimple(map, prefix + "ProjectId", this.ProjectId);
            this.SetParamArrayObj(map, prefix + "Tags.", this.Tags);
        }
    }
}

