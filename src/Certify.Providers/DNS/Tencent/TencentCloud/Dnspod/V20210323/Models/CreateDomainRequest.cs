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

    public class CreateDomainRequest : AbstractModel
    {
        
        /// <summary>
        /// 域名
        /// </summary>
        [JsonProperty("Domain")]
        public string Domain{ get; set; }

        /// <summary>
        /// 域名分组ID。可以通过接口DescribeDomainGroupList查看当前域名分组信息
        /// </summary>
        [JsonProperty("GroupId")]
        public ulong? GroupId{ get; set; }

        /// <summary>
        /// 是否星标域名，”yes”、”no” 分别代表是和否。
        /// </summary>
        [JsonProperty("IsMark")]
        public string IsMark{ get; set; }

        /// <summary>
        /// 添加子域名时，是否迁移相关父域名的解析记录。不传默认为 true
        /// </summary>
        [JsonProperty("TransferSubDomain")]
        public bool? TransferSubDomain{ get; set; }

        /// <summary>
        /// 域名绑定的标签
        /// </summary>
        [JsonProperty("Tags")]
        public TagItem[] Tags{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamSimple(map, prefix + "Domain", this.Domain);
            this.SetParamSimple(map, prefix + "GroupId", this.GroupId);
            this.SetParamSimple(map, prefix + "IsMark", this.IsMark);
            this.SetParamSimple(map, prefix + "TransferSubDomain", this.TransferSubDomain);
            this.SetParamArrayObj(map, prefix + "Tags.", this.Tags);
        }
    }
}

