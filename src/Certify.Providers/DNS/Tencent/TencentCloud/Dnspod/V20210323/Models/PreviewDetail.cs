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

    public class PreviewDetail : AbstractModel
    {
        
        /// <summary>
        /// 域名
        /// </summary>
        [JsonProperty("Name")]
        public string Name{ get; set; }

        /// <summary>
        /// 域名套餐代码
        /// </summary>
        [JsonProperty("Grade")]
        public string Grade{ get; set; }

        /// <summary>
        /// 域名套餐名称
        /// </summary>
        [JsonProperty("GradeTitle")]
        public string GradeTitle{ get; set; }

        /// <summary>
        /// 域名记录数
        /// </summary>
        [JsonProperty("Records")]
        public ulong? Records{ get; set; }

        /// <summary>
        /// 域名停靠状态。0 未开启 1 已开启 2 已暂停
        /// </summary>
        [JsonProperty("DomainParkingStatus")]
        public ulong? DomainParkingStatus{ get; set; }

        /// <summary>
        /// 自定义线路数量
        /// </summary>
        [JsonProperty("LineCount")]
        public ulong? LineCount{ get; set; }

        /// <summary>
        /// 自定义线路分组数量
        /// </summary>
        [JsonProperty("LineGroupCount")]
        public ulong? LineGroupCount{ get; set; }

        /// <summary>
        /// 域名别名数量
        /// </summary>
        [JsonProperty("AliasCount")]
        public ulong? AliasCount{ get; set; }

        /// <summary>
        /// 允许添加的最大域名别名数量
        /// </summary>
        [JsonProperty("MaxAliasCount")]
        public ulong? MaxAliasCount{ get; set; }

        /// <summary>
        /// 昨天的解析量
        /// </summary>
        [JsonProperty("ResolveCount")]
        public ulong? ResolveCount{ get; set; }

        /// <summary>
        /// 增值服务数量
        /// </summary>
        [JsonProperty("VASCount")]
        public ulong? VASCount{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamSimple(map, prefix + "Name", this.Name);
            this.SetParamSimple(map, prefix + "Grade", this.Grade);
            this.SetParamSimple(map, prefix + "GradeTitle", this.GradeTitle);
            this.SetParamSimple(map, prefix + "Records", this.Records);
            this.SetParamSimple(map, prefix + "DomainParkingStatus", this.DomainParkingStatus);
            this.SetParamSimple(map, prefix + "LineCount", this.LineCount);
            this.SetParamSimple(map, prefix + "LineGroupCount", this.LineGroupCount);
            this.SetParamSimple(map, prefix + "AliasCount", this.AliasCount);
            this.SetParamSimple(map, prefix + "MaxAliasCount", this.MaxAliasCount);
            this.SetParamSimple(map, prefix + "ResolveCount", this.ResolveCount);
            this.SetParamSimple(map, prefix + "VASCount", this.VASCount);
        }
    }
}

