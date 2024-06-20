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

    public class DescribeRecordFilterListRequest : AbstractModel
    {
        
        /// <summary>
        /// 要获取的解析记录所属的域名。
        /// </summary>
        [JsonProperty("Domain")]
        public string Domain{ get; set; }

        /// <summary>
        /// 要获取的解析记录所属的域名 Id，如果传了 DomainId，系统将会忽略 Domain 参数。 可以通过接口 DescribeDomainList 查到所有的 Domain 以及 DomainId。
        /// </summary>
        [JsonProperty("DomainId")]
        public ulong? DomainId{ get; set; }

        /// <summary>
        /// 根据解析记录的主机头获取解析记录。默认模糊匹配。可以通过设置 IsExactSubdomain 参数为 true 进行精确查找。
        /// </summary>
        [JsonProperty("SubDomain")]
        public string SubDomain{ get; set; }

        /// <summary>
        /// 获取某些类型的解析记录，如 A，CNAME，NS，AAAA，显性URL，隐性URL，CAA，SPF等。
        /// </summary>
        [JsonProperty("RecordType")]
        public string[] RecordType{ get; set; }

        /// <summary>
        /// 获取某些线路ID的解析记录。可以通过接口 DescribeRecordLineList 查看当前域名允许的线路信息。
        /// </summary>
        [JsonProperty("RecordLine")]
        public string[] RecordLine{ get; set; }

        /// <summary>
        /// 获取某些分组下的解析记录时，传这个分组 Id。可以通过接口 DescribeRecordGroupList 接口 GroupId 字段获取。
        /// </summary>
        [JsonProperty("GroupId")]
        public ulong?[] GroupId{ get; set; }

        /// <summary>
        /// 通过关键字搜索解析记录，当前支持搜索主机头和记录值
        /// </summary>
        [JsonProperty("Keyword")]
        public string Keyword{ get; set; }

        /// <summary>
        /// 排序字段，支持 NAME，LINE，TYPE，VALUE，WEIGHT，MX，TTL，UPDATED_ON 几个字段。
        /// NAME：解析记录的主机头
        /// LINE：解析记录线路
        /// TYPE：解析记录类型
        /// VALUE：解析记录值
        /// WEIGHT：权重
        /// MX：MX 优先级
        /// TTL：解析记录缓存时间
        /// UPDATED_ON：解析记录更新时间
        /// </summary>
        [JsonProperty("SortField")]
        public string SortField{ get; set; }

        /// <summary>
        /// 排序方式，升序：ASC，降序：DESC。默认值为ASC。
        /// </summary>
        [JsonProperty("SortType")]
        public string SortType{ get; set; }

        /// <summary>
        /// 偏移量，默认值为0。
        /// </summary>
        [JsonProperty("Offset")]
        public ulong? Offset{ get; set; }

        /// <summary>
        /// 限制数量，当前Limit最大支持3000。默认值为100。
        /// </summary>
        [JsonProperty("Limit")]
        public ulong? Limit{ get; set; }

        /// <summary>
        /// 根据解析记录的值获取解析记录
        /// </summary>
        [JsonProperty("RecordValue")]
        public string RecordValue{ get; set; }

        /// <summary>
        /// 根据解析记录的状态获取解析记录。可取值为 ENABLE，DISABLE。
        /// ENABLE：正常 
        /// DISABLE：暂停 
        /// </summary>
        [JsonProperty("RecordStatus")]
        public string[] RecordStatus{ get; set; }

        /// <summary>
        /// 要获取解析记录权重查询区间起点。
        /// </summary>
        [JsonProperty("WeightBegin")]
        public ulong? WeightBegin{ get; set; }

        /// <summary>
        /// 要获取解析记录权重查询区间终点。
        /// </summary>
        [JsonProperty("WeightEnd")]
        public ulong? WeightEnd{ get; set; }

        /// <summary>
        /// 要获取解析记录 MX 优先级查询区间起点。
        /// </summary>
        [JsonProperty("MXBegin")]
        public ulong? MXBegin{ get; set; }

        /// <summary>
        /// 要获取解析记录 MX 优先级查询区间终点。
        /// </summary>
        [JsonProperty("MXEnd")]
        public ulong? MXEnd{ get; set; }

        /// <summary>
        /// 要获取解析记录 TTL 查询区间起点。
        /// </summary>
        [JsonProperty("TTLBegin")]
        public ulong? TTLBegin{ get; set; }

        /// <summary>
        /// 要获取解析记录 TTL 查询区间终点。
        /// </summary>
        [JsonProperty("TTLEnd")]
        public ulong? TTLEnd{ get; set; }

        /// <summary>
        /// 要获取解析记录更新时间查询区间起点。
        /// </summary>
        [JsonProperty("UpdatedAtBegin")]
        public string UpdatedAtBegin{ get; set; }

        /// <summary>
        /// 要获取解析记录更新时间查询区间终点。
        /// </summary>
        [JsonProperty("UpdatedAtEnd")]
        public string UpdatedAtEnd{ get; set; }

        /// <summary>
        /// 根据解析记录的备注获取解析记录。
        /// </summary>
        [JsonProperty("Remark")]
        public string Remark{ get; set; }

        /// <summary>
        /// 是否根据 Subdomain 参数进行精确查找。
        /// </summary>
        [JsonProperty("IsExactSubDomain")]
        public bool? IsExactSubDomain{ get; set; }

        /// <summary>
        /// 项目ID
        /// </summary>
        [JsonProperty("ProjectId")]
        public long? ProjectId{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamSimple(map, prefix + "Domain", this.Domain);
            this.SetParamSimple(map, prefix + "DomainId", this.DomainId);
            this.SetParamSimple(map, prefix + "SubDomain", this.SubDomain);
            this.SetParamArraySimple(map, prefix + "RecordType.", this.RecordType);
            this.SetParamArraySimple(map, prefix + "RecordLine.", this.RecordLine);
            this.SetParamArraySimple(map, prefix + "GroupId.", this.GroupId);
            this.SetParamSimple(map, prefix + "Keyword", this.Keyword);
            this.SetParamSimple(map, prefix + "SortField", this.SortField);
            this.SetParamSimple(map, prefix + "SortType", this.SortType);
            this.SetParamSimple(map, prefix + "Offset", this.Offset);
            this.SetParamSimple(map, prefix + "Limit", this.Limit);
            this.SetParamSimple(map, prefix + "RecordValue", this.RecordValue);
            this.SetParamArraySimple(map, prefix + "RecordStatus.", this.RecordStatus);
            this.SetParamSimple(map, prefix + "WeightBegin", this.WeightBegin);
            this.SetParamSimple(map, prefix + "WeightEnd", this.WeightEnd);
            this.SetParamSimple(map, prefix + "MXBegin", this.MXBegin);
            this.SetParamSimple(map, prefix + "MXEnd", this.MXEnd);
            this.SetParamSimple(map, prefix + "TTLBegin", this.TTLBegin);
            this.SetParamSimple(map, prefix + "TTLEnd", this.TTLEnd);
            this.SetParamSimple(map, prefix + "UpdatedAtBegin", this.UpdatedAtBegin);
            this.SetParamSimple(map, prefix + "UpdatedAtEnd", this.UpdatedAtEnd);
            this.SetParamSimple(map, prefix + "Remark", this.Remark);
            this.SetParamSimple(map, prefix + "IsExactSubDomain", this.IsExactSubDomain);
            this.SetParamSimple(map, prefix + "ProjectId", this.ProjectId);
        }
    }
}

