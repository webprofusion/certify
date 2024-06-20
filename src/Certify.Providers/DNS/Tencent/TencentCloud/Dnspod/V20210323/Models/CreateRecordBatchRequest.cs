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

    public class CreateRecordBatchRequest : AbstractModel
    {
        
        /// <summary>
        /// 域名ID，多个 domain_id 用英文逗号进行分割。
        /// </summary>
        [JsonProperty("DomainIdList")]
        public string[] DomainIdList{ get; set; }

        /// <summary>
        /// 记录数组
        /// </summary>
        [JsonProperty("RecordList")]
        public AddRecordBatch[] RecordList{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamArraySimple(map, prefix + "DomainIdList.", this.DomainIdList);
            this.SetParamArrayObj(map, prefix + "RecordList.", this.RecordList);
        }
    }
}

