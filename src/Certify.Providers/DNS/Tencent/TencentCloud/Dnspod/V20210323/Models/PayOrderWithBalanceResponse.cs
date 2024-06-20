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

    public class PayOrderWithBalanceResponse : AbstractModel
    {
        
        /// <summary>
        /// 此次操作支付成功的订单id数组
        /// </summary>
        [JsonProperty("DealIdList")]
        public string[] DealIdList{ get; set; }

        /// <summary>
        /// 此次操作支付成功的大订单号数组
        /// </summary>
        [JsonProperty("BigDealIdList")]
        public string[] BigDealIdList{ get; set; }

        /// <summary>
        /// 此次操作支付成功的订单号数组
        /// </summary>
        [JsonProperty("DealNameList")]
        public string[] DealNameList{ get; set; }

        /// <summary>
        /// 唯一请求 ID，由服务端生成，每次请求都会返回（若请求因其他原因未能抵达服务端，则该次请求不会获得 RequestId）。定位问题时需要提供该次请求的 RequestId。
        /// </summary>
        [JsonProperty("RequestId")]
        public string RequestId{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamArraySimple(map, prefix + "DealIdList.", this.DealIdList);
            this.SetParamArraySimple(map, prefix + "BigDealIdList.", this.BigDealIdList);
            this.SetParamArraySimple(map, prefix + "DealNameList.", this.DealNameList);
            this.SetParamSimple(map, prefix + "RequestId", this.RequestId);
        }
    }
}

