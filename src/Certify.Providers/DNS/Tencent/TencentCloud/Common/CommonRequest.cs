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

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TencentCloud.Common
{
    public sealed class CommonRequest : AbstractModel, ISerializable
    {
        private readonly JObject _body;

        public CommonRequest(string jsonStr)
        {
            _body = JObject.Parse(jsonStr);
        }

        public CommonRequest(object serializable)
        {
            _body = JObject.Parse(JsonConvert.SerializeObject(serializable));
        }

        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
            ToMapFromValue(map, prefix, _body);
        }

        private void ToMapFromValue(Dictionary<string, string> map, string prefix, JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    foreach (var kv in token.Value<JObject>())
                    {
                        if (kv.Value == null)
                            continue;
                        ToMapFromValue(map, prefix == "" ? kv.Key : prefix + "." + kv.Key, kv.Value);
                    }

                    break;
                case JTokenType.Array:
                    var i = -1;
                    foreach (var v in token.Value<JArray>())
                    {
                        i++;
                        if (v == null)
                            continue;
                        ToMapFromValue(map, $"{prefix}.{i}", v);
                    }

                    break;
                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.String:
                case JTokenType.Boolean:
                    SetParamSimple(map, prefix, token);
                    break;
                case JTokenType.Null:
                    break;
                case JTokenType.None:
                case JTokenType.Constructor:
                case JTokenType.Property:
                case JTokenType.Comment:
                case JTokenType.Undefined:
                case JTokenType.Date:
                case JTokenType.Raw:
                case JTokenType.Bytes:
                case JTokenType.Guid:
                case JTokenType.Uri:
                case JTokenType.TimeSpan:
                default:
                    throw new TencentCloudSDKException($"invalid json value {token.Type}");
            }
        }

        public string Serialize()
        {
            return JsonConvert.SerializeObject(_body);
        }
    }
}