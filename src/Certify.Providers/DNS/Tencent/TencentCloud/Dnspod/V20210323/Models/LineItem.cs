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

    public class LineItem : AbstractModel
    {
        
        /// <summary>
        /// 解析线路名称。
        /// </summary>
        [JsonProperty("LineName")]
        public string LineName{ get; set; }

        /// <summary>
        /// 解析线路 ID。
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("LineId")]
        public string LineId{ get; set; }

        /// <summary>
        /// 当前线路在当前域名下是否可用。
        /// </summary>
        [JsonProperty("Useful")]
        public bool? Useful{ get; set; }

        /// <summary>
        /// 当前线路最低套餐等级要求。
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("Grade")]
        public string Grade{ get; set; }

        /// <summary>
        /// 当前线路分类下的子线路列表。
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("SubGroup")]
        public LineItem[] SubGroup{ get; set; }

        /// <summary>
        /// 自定义线路分组内包含的线路。
        /// 注意：此字段可能返回 null，表示取不到有效值。
        /// </summary>
        [JsonProperty("Lines")]
        public string[] Lines{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamSimple(map, prefix + "LineName", this.LineName);
            this.SetParamSimple(map, prefix + "LineId", this.LineId);
            this.SetParamSimple(map, prefix + "Useful", this.Useful);
            this.SetParamSimple(map, prefix + "Grade", this.Grade);
            this.SetParamArrayObj(map, prefix + "SubGroup.", this.SubGroup);
            this.SetParamArraySimple(map, prefix + "Lines.", this.Lines);
        }
    }
}

