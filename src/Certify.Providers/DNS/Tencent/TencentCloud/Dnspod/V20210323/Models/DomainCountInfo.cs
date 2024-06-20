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

    public class DomainCountInfo : AbstractModel
    {
        
        /// <summary>
        /// 符合条件的域名数量
        /// </summary>
        [JsonProperty("DomainTotal")]
        public ulong? DomainTotal{ get; set; }

        /// <summary>
        /// 用户可以查看的所有域名数量
        /// </summary>
        [JsonProperty("AllTotal")]
        public ulong? AllTotal{ get; set; }

        /// <summary>
        /// 用户账号添加的域名数量
        /// </summary>
        [JsonProperty("MineTotal")]
        public ulong? MineTotal{ get; set; }

        /// <summary>
        /// 共享给用户的域名数量
        /// </summary>
        [JsonProperty("ShareTotal")]
        public ulong? ShareTotal{ get; set; }

        /// <summary>
        /// 付费域名数量
        /// </summary>
        [JsonProperty("VipTotal")]
        public ulong? VipTotal{ get; set; }

        /// <summary>
        /// 暂停的域名数量
        /// </summary>
        [JsonProperty("PauseTotal")]
        public ulong? PauseTotal{ get; set; }

        /// <summary>
        /// dns设置错误的域名数量
        /// </summary>
        [JsonProperty("ErrorTotal")]
        public ulong? ErrorTotal{ get; set; }

        /// <summary>
        /// 锁定的域名数量
        /// </summary>
        [JsonProperty("LockTotal")]
        public ulong? LockTotal{ get; set; }

        /// <summary>
        /// 封禁的域名数量
        /// </summary>
        [JsonProperty("SpamTotal")]
        public ulong? SpamTotal{ get; set; }

        /// <summary>
        /// 30天内即将到期的域名数量
        /// </summary>
        [JsonProperty("VipExpire")]
        public ulong? VipExpire{ get; set; }

        /// <summary>
        /// 分享给其它人的域名数量
        /// </summary>
        [JsonProperty("ShareOutTotal")]
        public ulong? ShareOutTotal{ get; set; }

        /// <summary>
        /// 指定分组内的域名数量
        /// </summary>
        [JsonProperty("GroupTotal")]
        public ulong? GroupTotal{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamSimple(map, prefix + "DomainTotal", this.DomainTotal);
            this.SetParamSimple(map, prefix + "AllTotal", this.AllTotal);
            this.SetParamSimple(map, prefix + "MineTotal", this.MineTotal);
            this.SetParamSimple(map, prefix + "ShareTotal", this.ShareTotal);
            this.SetParamSimple(map, prefix + "VipTotal", this.VipTotal);
            this.SetParamSimple(map, prefix + "PauseTotal", this.PauseTotal);
            this.SetParamSimple(map, prefix + "ErrorTotal", this.ErrorTotal);
            this.SetParamSimple(map, prefix + "LockTotal", this.LockTotal);
            this.SetParamSimple(map, prefix + "SpamTotal", this.SpamTotal);
            this.SetParamSimple(map, prefix + "VipExpire", this.VipExpire);
            this.SetParamSimple(map, prefix + "ShareOutTotal", this.ShareOutTotal);
            this.SetParamSimple(map, prefix + "GroupTotal", this.GroupTotal);
        }
    }
}

