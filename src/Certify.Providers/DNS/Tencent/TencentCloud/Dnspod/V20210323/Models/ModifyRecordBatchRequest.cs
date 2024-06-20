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

    public class ModifyRecordBatchRequest : AbstractModel
    {
        
        /// <summary>
        /// 记录ID数组。可以通过接口DescribeRecordList查到所有的解析记录列表以及对应的RecordId
        /// </summary>
        [JsonProperty("RecordIdList")]
        public ulong?[] RecordIdList{ get; set; }

        /// <summary>
        /// 要修改的字段，可选值为 [“sub_domain”、”record_type”、”area”、”value”、”mx”、”ttl”、”status”] 中的某一个。
        /// </summary>
        [JsonProperty("Change")]
        public string Change{ get; set; }

        /// <summary>
        /// 修改为，具体依赖 change 字段，必填参数。
        /// </summary>
        [JsonProperty("ChangeTo")]
        public string ChangeTo{ get; set; }

        /// <summary>
        /// 要修改到的记录值，仅当 change 字段为 “record_type” 时为必填参数。
        /// </summary>
        [JsonProperty("Value")]
        public string Value{ get; set; }

        /// <summary>
        /// MX记录优先级，仅当修改为 MX 记录时为必填参数。
        /// </summary>
        [JsonProperty("MX")]
        public string MX{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamArraySimple(map, prefix + "RecordIdList.", this.RecordIdList);
            this.SetParamSimple(map, prefix + "Change", this.Change);
            this.SetParamSimple(map, prefix + "ChangeTo", this.ChangeTo);
            this.SetParamSimple(map, prefix + "Value", this.Value);
            this.SetParamSimple(map, prefix + "MX", this.MX);
        }
    }
}

