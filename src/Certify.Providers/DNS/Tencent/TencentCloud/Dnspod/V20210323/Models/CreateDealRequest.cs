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

    public class CreateDealRequest : AbstractModel
    {
        
        /// <summary>
        /// 询价类型，1 新购，2 续费，3 套餐升级（增值服务暂时只支持新购）
        /// </summary>
        [JsonProperty("DealType")]
        public ulong? DealType{ get; set; }

        /// <summary>
        /// 商品类型，1 域名套餐 2 增值服务
        /// </summary>
        [JsonProperty("GoodsType")]
        public ulong? GoodsType{ get; set; }

        /// <summary>
        /// 套餐类型：
        /// DP_PLUS：专业版
        /// DP_EXPERT：企业版
        /// DP_ULTRA：尊享版
        /// 
        /// 增值服务类型
        /// LB：负载均衡
        /// URL：URL转发
        /// DMONITOR_TASKS：D监控任务数
        /// DMONITOR_IP：D监控备用 IP 数
        /// CUSTOMLINE：自定义线路数
        /// </summary>
        [JsonProperty("GoodsChildType")]
        public string GoodsChildType{ get; set; }

        /// <summary>
        /// 增值服务购买数量，如果是域名套餐固定为1，如果是增值服务则按以下规则：
        /// 负载均衡、D监控任务数、D监控备用 IP 数、自定义线路数、URL 转发（必须是5的正整数倍，如 5、10、15 等）
        /// </summary>
        [JsonProperty("GoodsNum")]
        public ulong? GoodsNum{ get; set; }

        /// <summary>
        /// 是否开启自动续费，1 开启，2 不开启（增值服务暂不支持自动续费），默认值为 2 不开启
        /// </summary>
        [JsonProperty("AutoRenew")]
        public ulong? AutoRenew{ get; set; }

        /// <summary>
        /// 需要绑定套餐的域名，如 dnspod.cn，如果是续费或升级，domain 参数必须要传，新购可不传。
        /// </summary>
        [JsonProperty("Domain")]
        public string Domain{ get; set; }

        /// <summary>
        /// 套餐时长：
        /// 1. 套餐以月为单位（按月只能是 3、6 还有 12 的倍数），套餐例如购买一年则传12，最大120 。（续费最低一年）
        /// 2. 升级套餐时不需要传。
        /// 3. 增值服务的时长单位为年，买一年传1（增值服务新购按年只能是 1，增值服务续费最大为 10）
        /// </summary>
        [JsonProperty("TimeSpan")]
        public ulong? TimeSpan{ get; set; }

        /// <summary>
        /// 套餐类型，需要升级到的套餐类型，只有升级时需要。
        /// </summary>
        [JsonProperty("NewPackageType")]
        public string NewPackageType{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamSimple(map, prefix + "DealType", this.DealType);
            this.SetParamSimple(map, prefix + "GoodsType", this.GoodsType);
            this.SetParamSimple(map, prefix + "GoodsChildType", this.GoodsChildType);
            this.SetParamSimple(map, prefix + "GoodsNum", this.GoodsNum);
            this.SetParamSimple(map, prefix + "AutoRenew", this.AutoRenew);
            this.SetParamSimple(map, prefix + "Domain", this.Domain);
            this.SetParamSimple(map, prefix + "TimeSpan", this.TimeSpan);
            this.SetParamSimple(map, prefix + "NewPackageType", this.NewPackageType);
        }
    }
}

