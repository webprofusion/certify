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

    public class UserInfo : AbstractModel
    {
        
        /// <summary>
        /// 用户昵称
        /// </summary>
        [JsonProperty("Nick")]
        public string Nick{ get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        [JsonProperty("Id")]
        public long? Id{ get; set; }

        /// <summary>
        /// 用户账号, 邮箱格式
        /// </summary>
        [JsonProperty("Email")]
        public string Email{ get; set; }

        /// <summary>
        /// 账号状态：”enabled”: 正常；”disabled”: 被封禁
        /// </summary>
        [JsonProperty("Status")]
        public string Status{ get; set; }

        /// <summary>
        /// 电话号码
        /// </summary>
        [JsonProperty("Telephone")]
        public string Telephone{ get; set; }

        /// <summary>
        /// 邮箱是否通过验证：”yes”: 通过；”no”: 未通过
        /// </summary>
        [JsonProperty("EmailVerified")]
        public string EmailVerified{ get; set; }

        /// <summary>
        /// 手机是否通过验证：”yes”: 通过；”no”: 未通过
        /// </summary>
        [JsonProperty("TelephoneVerified")]
        public string TelephoneVerified{ get; set; }

        /// <summary>
        /// 账号等级, 按照用户账号下域名等级排序, 选取一个最高等级为账号等级, 具体对应情况参见域名等级。
        /// </summary>
        [JsonProperty("UserGrade")]
        public string UserGrade{ get; set; }

        /// <summary>
        /// 用户名称, 企业用户对应为公司名称
        /// </summary>
        [JsonProperty("RealName")]
        public string RealName{ get; set; }

        /// <summary>
        /// 是否绑定微信：”yes”: 通过；”no”: 未通过
        /// </summary>
        [JsonProperty("WechatBinded")]
        public string WechatBinded{ get; set; }

        /// <summary>
        /// 用户UIN
        /// </summary>
        [JsonProperty("Uin")]
        public long? Uin{ get; set; }

        /// <summary>
        /// 所属 DNS 服务器
        /// </summary>
        [JsonProperty("FreeNs")]
        public string[] FreeNs{ get; set; }


        /// <summary>
        /// For internal usage only. DO NOT USE IT.
        /// </summary>
        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            this.SetParamSimple(map, prefix + "Nick", this.Nick);
            this.SetParamSimple(map, prefix + "Id", this.Id);
            this.SetParamSimple(map, prefix + "Email", this.Email);
            this.SetParamSimple(map, prefix + "Status", this.Status);
            this.SetParamSimple(map, prefix + "Telephone", this.Telephone);
            this.SetParamSimple(map, prefix + "EmailVerified", this.EmailVerified);
            this.SetParamSimple(map, prefix + "TelephoneVerified", this.TelephoneVerified);
            this.SetParamSimple(map, prefix + "UserGrade", this.UserGrade);
            this.SetParamSimple(map, prefix + "RealName", this.RealName);
            this.SetParamSimple(map, prefix + "WechatBinded", this.WechatBinded);
            this.SetParamSimple(map, prefix + "Uin", this.Uin);
            this.SetParamArraySimple(map, prefix + "FreeNs.", this.FreeNs);
        }
    }
}

