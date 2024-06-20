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

namespace TencentCloud.Dnspod.V20210323
{

   using Newtonsoft.Json;
   using System.Threading.Tasks;
   using TencentCloud.Common;
   using TencentCloud.Common.Profile;
   using TencentCloud.Dnspod.V20210323.Models;

   public class DnspodClient : AbstractClient{

       private const string endpoint = "dnspod.tencentcloudapi.com";
       private const string version = "2021-03-23";
       private const string sdkVersion = "SDK_NET_3.0.1029";

        /// <summary>
        /// Client constructor.
        /// </summary>
        /// <param name="credential">Credentials.</param>
        /// <param name="region">Region name, such as "ap-guangzhou".</param>
        public DnspodClient(Credential credential, string region)
            : this(credential, region, new ClientProfile { Language = Language.ZH_CN })
        {

        }

        /// <summary>
        /// Client Constructor.
        /// </summary>
        /// <param name="credential">Credentials.</param>
        /// <param name="region">Region name, such as "ap-guangzhou".</param>
        /// <param name="profile">Client profiles.</param>
        public DnspodClient(Credential credential, string region, ClientProfile profile)
            : base(endpoint, version, credential, region, profile)
        {
            SdkVersion = sdkVersion;
        }

        /// <summary>
        /// 回滚前检查单条记录
        /// </summary>
        /// <param name="req"><see cref="CheckRecordSnapshotRollbackRequest"/></param>
        /// <returns><see cref="CheckRecordSnapshotRollbackResponse"/></returns>
        public Task<CheckRecordSnapshotRollbackResponse> CheckRecordSnapshotRollback(CheckRecordSnapshotRollbackRequest req)
        {
            return InternalRequestAsync<CheckRecordSnapshotRollbackResponse>(req, "CheckRecordSnapshotRollback");
        }

        /// <summary>
        /// 回滚前检查单条记录
        /// </summary>
        /// <param name="req"><see cref="CheckRecordSnapshotRollbackRequest"/></param>
        /// <returns><see cref="CheckRecordSnapshotRollbackResponse"/></returns>
        public CheckRecordSnapshotRollbackResponse CheckRecordSnapshotRollbackSync(CheckRecordSnapshotRollbackRequest req)
        {
            return InternalRequestAsync<CheckRecordSnapshotRollbackResponse>(req, "CheckRecordSnapshotRollback")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 快照回滚前检查
        /// </summary>
        /// <param name="req"><see cref="CheckSnapshotRollbackRequest"/></param>
        /// <returns><see cref="CheckSnapshotRollbackResponse"/></returns>
        public Task<CheckSnapshotRollbackResponse> CheckSnapshotRollback(CheckSnapshotRollbackRequest req)
        {
            return InternalRequestAsync<CheckSnapshotRollbackResponse>(req, "CheckSnapshotRollback");
        }

        /// <summary>
        /// 快照回滚前检查
        /// </summary>
        /// <param name="req"><see cref="CheckSnapshotRollbackRequest"/></param>
        /// <returns><see cref="CheckSnapshotRollbackResponse"/></returns>
        public CheckSnapshotRollbackResponse CheckSnapshotRollbackSync(CheckSnapshotRollbackRequest req)
        {
            return InternalRequestAsync<CheckSnapshotRollbackResponse>(req, "CheckSnapshotRollback")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// DNSPod商品下单
        /// </summary>
        /// <param name="req"><see cref="CreateDealRequest"/></param>
        /// <returns><see cref="CreateDealResponse"/></returns>
        public Task<CreateDealResponse> CreateDeal(CreateDealRequest req)
        {
            return InternalRequestAsync<CreateDealResponse>(req, "CreateDeal");
        }

        /// <summary>
        /// DNSPod商品下单
        /// </summary>
        /// <param name="req"><see cref="CreateDealRequest"/></param>
        /// <returns><see cref="CreateDealResponse"/></returns>
        public CreateDealResponse CreateDealSync(CreateDealRequest req)
        {
            return InternalRequestAsync<CreateDealResponse>(req, "CreateDeal")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 添加域名
        /// </summary>
        /// <param name="req"><see cref="CreateDomainRequest"/></param>
        /// <returns><see cref="CreateDomainResponse"/></returns>
        public Task<CreateDomainResponse> CreateDomain(CreateDomainRequest req)
        {
            return InternalRequestAsync<CreateDomainResponse>(req, "CreateDomain");
        }

        /// <summary>
        /// 添加域名
        /// </summary>
        /// <param name="req"><see cref="CreateDomainRequest"/></param>
        /// <returns><see cref="CreateDomainResponse"/></returns>
        public CreateDomainResponse CreateDomainSync(CreateDomainRequest req)
        {
            return InternalRequestAsync<CreateDomainResponse>(req, "CreateDomain")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 创建域名别名
        /// </summary>
        /// <param name="req"><see cref="CreateDomainAliasRequest"/></param>
        /// <returns><see cref="CreateDomainAliasResponse"/></returns>
        public Task<CreateDomainAliasResponse> CreateDomainAlias(CreateDomainAliasRequest req)
        {
            return InternalRequestAsync<CreateDomainAliasResponse>(req, "CreateDomainAlias");
        }

        /// <summary>
        /// 创建域名别名
        /// </summary>
        /// <param name="req"><see cref="CreateDomainAliasRequest"/></param>
        /// <returns><see cref="CreateDomainAliasResponse"/></returns>
        public CreateDomainAliasResponse CreateDomainAliasSync(CreateDomainAliasRequest req)
        {
            return InternalRequestAsync<CreateDomainAliasResponse>(req, "CreateDomainAlias")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 批量添加域名
        /// </summary>
        /// <param name="req"><see cref="CreateDomainBatchRequest"/></param>
        /// <returns><see cref="CreateDomainBatchResponse"/></returns>
        public Task<CreateDomainBatchResponse> CreateDomainBatch(CreateDomainBatchRequest req)
        {
            return InternalRequestAsync<CreateDomainBatchResponse>(req, "CreateDomainBatch");
        }

        /// <summary>
        /// 批量添加域名
        /// </summary>
        /// <param name="req"><see cref="CreateDomainBatchRequest"/></param>
        /// <returns><see cref="CreateDomainBatchResponse"/></returns>
        public CreateDomainBatchResponse CreateDomainBatchSync(CreateDomainBatchRequest req)
        {
            return InternalRequestAsync<CreateDomainBatchResponse>(req, "CreateDomainBatch")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 创建域名的自定义线路
        /// </summary>
        /// <param name="req"><see cref="CreateDomainCustomLineRequest"/></param>
        /// <returns><see cref="CreateDomainCustomLineResponse"/></returns>
        public Task<CreateDomainCustomLineResponse> CreateDomainCustomLine(CreateDomainCustomLineRequest req)
        {
            return InternalRequestAsync<CreateDomainCustomLineResponse>(req, "CreateDomainCustomLine");
        }

        /// <summary>
        /// 创建域名的自定义线路
        /// </summary>
        /// <param name="req"><see cref="CreateDomainCustomLineRequest"/></param>
        /// <returns><see cref="CreateDomainCustomLineResponse"/></returns>
        public CreateDomainCustomLineResponse CreateDomainCustomLineSync(CreateDomainCustomLineRequest req)
        {
            return InternalRequestAsync<CreateDomainCustomLineResponse>(req, "CreateDomainCustomLine")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 创建域名分组
        /// </summary>
        /// <param name="req"><see cref="CreateDomainGroupRequest"/></param>
        /// <returns><see cref="CreateDomainGroupResponse"/></returns>
        public Task<CreateDomainGroupResponse> CreateDomainGroup(CreateDomainGroupRequest req)
        {
            return InternalRequestAsync<CreateDomainGroupResponse>(req, "CreateDomainGroup");
        }

        /// <summary>
        /// 创建域名分组
        /// </summary>
        /// <param name="req"><see cref="CreateDomainGroupRequest"/></param>
        /// <returns><see cref="CreateDomainGroupResponse"/></returns>
        public CreateDomainGroupResponse CreateDomainGroupSync(CreateDomainGroupRequest req)
        {
            return InternalRequestAsync<CreateDomainGroupResponse>(req, "CreateDomainGroup")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 添加记录
        /// 备注：新添加的解析记录存在短暂的索引延迟，如果查询不到新增记录，请在 30 秒后重试
        /// </summary>
        /// <param name="req"><see cref="CreateRecordRequest"/></param>
        /// <returns><see cref="CreateRecordResponse"/></returns>
        public Task<CreateRecordResponse> CreateRecord(CreateRecordRequest req)
        {
            return InternalRequestAsync<CreateRecordResponse>(req, "CreateRecord");
        }

        /// <summary>
        /// 添加记录
        /// 备注：新添加的解析记录存在短暂的索引延迟，如果查询不到新增记录，请在 30 秒后重试
        /// </summary>
        /// <param name="req"><see cref="CreateRecordRequest"/></param>
        /// <returns><see cref="CreateRecordResponse"/></returns>
        public CreateRecordResponse CreateRecordSync(CreateRecordRequest req)
        {
            return InternalRequestAsync<CreateRecordResponse>(req, "CreateRecord")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 批量添加记录
        /// </summary>
        /// <param name="req"><see cref="CreateRecordBatchRequest"/></param>
        /// <returns><see cref="CreateRecordBatchResponse"/></returns>
        public Task<CreateRecordBatchResponse> CreateRecordBatch(CreateRecordBatchRequest req)
        {
            return InternalRequestAsync<CreateRecordBatchResponse>(req, "CreateRecordBatch");
        }

        /// <summary>
        /// 批量添加记录
        /// </summary>
        /// <param name="req"><see cref="CreateRecordBatchRequest"/></param>
        /// <returns><see cref="CreateRecordBatchResponse"/></returns>
        public CreateRecordBatchResponse CreateRecordBatchSync(CreateRecordBatchRequest req)
        {
            return InternalRequestAsync<CreateRecordBatchResponse>(req, "CreateRecordBatch")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 添加记录分组
        /// </summary>
        /// <param name="req"><see cref="CreateRecordGroupRequest"/></param>
        /// <returns><see cref="CreateRecordGroupResponse"/></returns>
        public Task<CreateRecordGroupResponse> CreateRecordGroup(CreateRecordGroupRequest req)
        {
            return InternalRequestAsync<CreateRecordGroupResponse>(req, "CreateRecordGroup");
        }

        /// <summary>
        /// 添加记录分组
        /// </summary>
        /// <param name="req"><see cref="CreateRecordGroupRequest"/></param>
        /// <returns><see cref="CreateRecordGroupResponse"/></returns>
        public CreateRecordGroupResponse CreateRecordGroupSync(CreateRecordGroupRequest req)
        {
            return InternalRequestAsync<CreateRecordGroupResponse>(req, "CreateRecordGroup")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 创建快照
        /// </summary>
        /// <param name="req"><see cref="CreateSnapshotRequest"/></param>
        /// <returns><see cref="CreateSnapshotResponse"/></returns>
        public Task<CreateSnapshotResponse> CreateSnapshot(CreateSnapshotRequest req)
        {
            return InternalRequestAsync<CreateSnapshotResponse>(req, "CreateSnapshot");
        }

        /// <summary>
        /// 创建快照
        /// </summary>
        /// <param name="req"><see cref="CreateSnapshotRequest"/></param>
        /// <returns><see cref="CreateSnapshotResponse"/></returns>
        public CreateSnapshotResponse CreateSnapshotSync(CreateSnapshotRequest req)
        {
            return InternalRequestAsync<CreateSnapshotResponse>(req, "CreateSnapshot")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 删除域名
        /// </summary>
        /// <param name="req"><see cref="DeleteDomainRequest"/></param>
        /// <returns><see cref="DeleteDomainResponse"/></returns>
        public Task<DeleteDomainResponse> DeleteDomain(DeleteDomainRequest req)
        {
            return InternalRequestAsync<DeleteDomainResponse>(req, "DeleteDomain");
        }

        /// <summary>
        /// 删除域名
        /// </summary>
        /// <param name="req"><see cref="DeleteDomainRequest"/></param>
        /// <returns><see cref="DeleteDomainResponse"/></returns>
        public DeleteDomainResponse DeleteDomainSync(DeleteDomainRequest req)
        {
            return InternalRequestAsync<DeleteDomainResponse>(req, "DeleteDomain")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 删除域名别名
        /// </summary>
        /// <param name="req"><see cref="DeleteDomainAliasRequest"/></param>
        /// <returns><see cref="DeleteDomainAliasResponse"/></returns>
        public Task<DeleteDomainAliasResponse> DeleteDomainAlias(DeleteDomainAliasRequest req)
        {
            return InternalRequestAsync<DeleteDomainAliasResponse>(req, "DeleteDomainAlias");
        }

        /// <summary>
        /// 删除域名别名
        /// </summary>
        /// <param name="req"><see cref="DeleteDomainAliasRequest"/></param>
        /// <returns><see cref="DeleteDomainAliasResponse"/></returns>
        public DeleteDomainAliasResponse DeleteDomainAliasSync(DeleteDomainAliasRequest req)
        {
            return InternalRequestAsync<DeleteDomainAliasResponse>(req, "DeleteDomainAlias")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 批量删除域名
        /// </summary>
        /// <param name="req"><see cref="DeleteDomainBatchRequest"/></param>
        /// <returns><see cref="DeleteDomainBatchResponse"/></returns>
        public Task<DeleteDomainBatchResponse> DeleteDomainBatch(DeleteDomainBatchRequest req)
        {
            return InternalRequestAsync<DeleteDomainBatchResponse>(req, "DeleteDomainBatch");
        }

        /// <summary>
        /// 批量删除域名
        /// </summary>
        /// <param name="req"><see cref="DeleteDomainBatchRequest"/></param>
        /// <returns><see cref="DeleteDomainBatchResponse"/></returns>
        public DeleteDomainBatchResponse DeleteDomainBatchSync(DeleteDomainBatchRequest req)
        {
            return InternalRequestAsync<DeleteDomainBatchResponse>(req, "DeleteDomainBatch")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 删除域名的自定义线路
        /// </summary>
        /// <param name="req"><see cref="DeleteDomainCustomLineRequest"/></param>
        /// <returns><see cref="DeleteDomainCustomLineResponse"/></returns>
        public Task<DeleteDomainCustomLineResponse> DeleteDomainCustomLine(DeleteDomainCustomLineRequest req)
        {
            return InternalRequestAsync<DeleteDomainCustomLineResponse>(req, "DeleteDomainCustomLine");
        }

        /// <summary>
        /// 删除域名的自定义线路
        /// </summary>
        /// <param name="req"><see cref="DeleteDomainCustomLineRequest"/></param>
        /// <returns><see cref="DeleteDomainCustomLineResponse"/></returns>
        public DeleteDomainCustomLineResponse DeleteDomainCustomLineSync(DeleteDomainCustomLineRequest req)
        {
            return InternalRequestAsync<DeleteDomainCustomLineResponse>(req, "DeleteDomainCustomLine")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 删除记录
        /// </summary>
        /// <param name="req"><see cref="DeleteRecordRequest"/></param>
        /// <returns><see cref="DeleteRecordResponse"/></returns>
        public Task<DeleteRecordResponse> DeleteRecord(DeleteRecordRequest req)
        {
            return InternalRequestAsync<DeleteRecordResponse>(req, "DeleteRecord");
        }

        /// <summary>
        /// 删除记录
        /// </summary>
        /// <param name="req"><see cref="DeleteRecordRequest"/></param>
        /// <returns><see cref="DeleteRecordResponse"/></returns>
        public DeleteRecordResponse DeleteRecordSync(DeleteRecordRequest req)
        {
            return InternalRequestAsync<DeleteRecordResponse>(req, "DeleteRecord")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 批量删除解析记录
        /// </summary>
        /// <param name="req"><see cref="DeleteRecordBatchRequest"/></param>
        /// <returns><see cref="DeleteRecordBatchResponse"/></returns>
        public Task<DeleteRecordBatchResponse> DeleteRecordBatch(DeleteRecordBatchRequest req)
        {
            return InternalRequestAsync<DeleteRecordBatchResponse>(req, "DeleteRecordBatch");
        }

        /// <summary>
        /// 批量删除解析记录
        /// </summary>
        /// <param name="req"><see cref="DeleteRecordBatchRequest"/></param>
        /// <returns><see cref="DeleteRecordBatchResponse"/></returns>
        public DeleteRecordBatchResponse DeleteRecordBatchSync(DeleteRecordBatchRequest req)
        {
            return InternalRequestAsync<DeleteRecordBatchResponse>(req, "DeleteRecordBatch")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 删除记录分组
        /// </summary>
        /// <param name="req"><see cref="DeleteRecordGroupRequest"/></param>
        /// <returns><see cref="DeleteRecordGroupResponse"/></returns>
        public Task<DeleteRecordGroupResponse> DeleteRecordGroup(DeleteRecordGroupRequest req)
        {
            return InternalRequestAsync<DeleteRecordGroupResponse>(req, "DeleteRecordGroup");
        }

        /// <summary>
        /// 删除记录分组
        /// </summary>
        /// <param name="req"><see cref="DeleteRecordGroupRequest"/></param>
        /// <returns><see cref="DeleteRecordGroupResponse"/></returns>
        public DeleteRecordGroupResponse DeleteRecordGroupSync(DeleteRecordGroupRequest req)
        {
            return InternalRequestAsync<DeleteRecordGroupResponse>(req, "DeleteRecordGroup")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 按账号删除域名共享
        /// </summary>
        /// <param name="req"><see cref="DeleteShareDomainRequest"/></param>
        /// <returns><see cref="DeleteShareDomainResponse"/></returns>
        public Task<DeleteShareDomainResponse> DeleteShareDomain(DeleteShareDomainRequest req)
        {
            return InternalRequestAsync<DeleteShareDomainResponse>(req, "DeleteShareDomain");
        }

        /// <summary>
        /// 按账号删除域名共享
        /// </summary>
        /// <param name="req"><see cref="DeleteShareDomainRequest"/></param>
        /// <returns><see cref="DeleteShareDomainResponse"/></returns>
        public DeleteShareDomainResponse DeleteShareDomainSync(DeleteShareDomainRequest req)
        {
            return InternalRequestAsync<DeleteShareDomainResponse>(req, "DeleteShareDomain")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 删除快照
        /// </summary>
        /// <param name="req"><see cref="DeleteSnapshotRequest"/></param>
        /// <returns><see cref="DeleteSnapshotResponse"/></returns>
        public Task<DeleteSnapshotResponse> DeleteSnapshot(DeleteSnapshotRequest req)
        {
            return InternalRequestAsync<DeleteSnapshotResponse>(req, "DeleteSnapshot");
        }

        /// <summary>
        /// 删除快照
        /// </summary>
        /// <param name="req"><see cref="DeleteSnapshotRequest"/></param>
        /// <returns><see cref="DeleteSnapshotResponse"/></returns>
        public DeleteSnapshotResponse DeleteSnapshotSync(DeleteSnapshotRequest req)
        {
            return InternalRequestAsync<DeleteSnapshotResponse>(req, "DeleteSnapshot")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取任务详情
        /// </summary>
        /// <param name="req"><see cref="DescribeBatchTaskRequest"/></param>
        /// <returns><see cref="DescribeBatchTaskResponse"/></returns>
        public Task<DescribeBatchTaskResponse> DescribeBatchTask(DescribeBatchTaskRequest req)
        {
            return InternalRequestAsync<DescribeBatchTaskResponse>(req, "DescribeBatchTask");
        }

        /// <summary>
        /// 获取任务详情
        /// </summary>
        /// <param name="req"><see cref="DescribeBatchTaskRequest"/></param>
        /// <returns><see cref="DescribeBatchTaskResponse"/></returns>
        public DescribeBatchTaskResponse DescribeBatchTaskSync(DescribeBatchTaskRequest req)
        {
            return InternalRequestAsync<DescribeBatchTaskResponse>(req, "DescribeBatchTask")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取域名信息
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainRequest"/></param>
        /// <returns><see cref="DescribeDomainResponse"/></returns>
        public Task<DescribeDomainResponse> DescribeDomain(DescribeDomainRequest req)
        {
            return InternalRequestAsync<DescribeDomainResponse>(req, "DescribeDomain");
        }

        /// <summary>
        /// 获取域名信息
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainRequest"/></param>
        /// <returns><see cref="DescribeDomainResponse"/></returns>
        public DescribeDomainResponse DescribeDomainSync(DescribeDomainRequest req)
        {
            return InternalRequestAsync<DescribeDomainResponse>(req, "DescribeDomain")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取域名别名列表
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainAliasListRequest"/></param>
        /// <returns><see cref="DescribeDomainAliasListResponse"/></returns>
        public Task<DescribeDomainAliasListResponse> DescribeDomainAliasList(DescribeDomainAliasListRequest req)
        {
            return InternalRequestAsync<DescribeDomainAliasListResponse>(req, "DescribeDomainAliasList");
        }

        /// <summary>
        /// 获取域名别名列表
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainAliasListRequest"/></param>
        /// <returns><see cref="DescribeDomainAliasListResponse"/></returns>
        public DescribeDomainAliasListResponse DescribeDomainAliasListSync(DescribeDomainAliasListRequest req)
        {
            return InternalRequestAsync<DescribeDomainAliasListResponse>(req, "DescribeDomainAliasList")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 统计各个域名的解析量，帮助您了解流量情况、时间段分布。支持查看近 3 个月内的统计情况
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainAnalyticsRequest"/></param>
        /// <returns><see cref="DescribeDomainAnalyticsResponse"/></returns>
        public Task<DescribeDomainAnalyticsResponse> DescribeDomainAnalytics(DescribeDomainAnalyticsRequest req)
        {
            return InternalRequestAsync<DescribeDomainAnalyticsResponse>(req, "DescribeDomainAnalytics");
        }

        /// <summary>
        /// 统计各个域名的解析量，帮助您了解流量情况、时间段分布。支持查看近 3 个月内的统计情况
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainAnalyticsRequest"/></param>
        /// <returns><see cref="DescribeDomainAnalyticsResponse"/></returns>
        public DescribeDomainAnalyticsResponse DescribeDomainAnalyticsSync(DescribeDomainAnalyticsRequest req)
        {
            return InternalRequestAsync<DescribeDomainAnalyticsResponse>(req, "DescribeDomainAnalytics")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取域名的自定义线路列表
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainCustomLineListRequest"/></param>
        /// <returns><see cref="DescribeDomainCustomLineListResponse"/></returns>
        public Task<DescribeDomainCustomLineListResponse> DescribeDomainCustomLineList(DescribeDomainCustomLineListRequest req)
        {
            return InternalRequestAsync<DescribeDomainCustomLineListResponse>(req, "DescribeDomainCustomLineList");
        }

        /// <summary>
        /// 获取域名的自定义线路列表
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainCustomLineListRequest"/></param>
        /// <returns><see cref="DescribeDomainCustomLineListResponse"/></returns>
        public DescribeDomainCustomLineListResponse DescribeDomainCustomLineListSync(DescribeDomainCustomLineListRequest req)
        {
            return InternalRequestAsync<DescribeDomainCustomLineListResponse>(req, "DescribeDomainCustomLineList")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取域名筛选列表
        /// 备注：新添加的解析记录存在短暂的索引延迟，如果查询不到新增记录，请在 30 秒后重试
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainFilterListRequest"/></param>
        /// <returns><see cref="DescribeDomainFilterListResponse"/></returns>
        public Task<DescribeDomainFilterListResponse> DescribeDomainFilterList(DescribeDomainFilterListRequest req)
        {
            return InternalRequestAsync<DescribeDomainFilterListResponse>(req, "DescribeDomainFilterList");
        }

        /// <summary>
        /// 获取域名筛选列表
        /// 备注：新添加的解析记录存在短暂的索引延迟，如果查询不到新增记录，请在 30 秒后重试
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainFilterListRequest"/></param>
        /// <returns><see cref="DescribeDomainFilterListResponse"/></returns>
        public DescribeDomainFilterListResponse DescribeDomainFilterListSync(DescribeDomainFilterListRequest req)
        {
            return InternalRequestAsync<DescribeDomainFilterListResponse>(req, "DescribeDomainFilterList")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取域名分组列表
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainGroupListRequest"/></param>
        /// <returns><see cref="DescribeDomainGroupListResponse"/></returns>
        public Task<DescribeDomainGroupListResponse> DescribeDomainGroupList(DescribeDomainGroupListRequest req)
        {
            return InternalRequestAsync<DescribeDomainGroupListResponse>(req, "DescribeDomainGroupList");
        }

        /// <summary>
        /// 获取域名分组列表
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainGroupListRequest"/></param>
        /// <returns><see cref="DescribeDomainGroupListResponse"/></returns>
        public DescribeDomainGroupListResponse DescribeDomainGroupListSync(DescribeDomainGroupListRequest req)
        {
            return InternalRequestAsync<DescribeDomainGroupListResponse>(req, "DescribeDomainGroupList")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取域名列表
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainListRequest"/></param>
        /// <returns><see cref="DescribeDomainListResponse"/></returns>
        public Task<DescribeDomainListResponse> DescribeDomainList(DescribeDomainListRequest req)
        {
            return InternalRequestAsync<DescribeDomainListResponse>(req, "DescribeDomainList");
        }

        /// <summary>
        /// 获取域名列表
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainListRequest"/></param>
        /// <returns><see cref="DescribeDomainListResponse"/></returns>
        public DescribeDomainListResponse DescribeDomainListSync(DescribeDomainListRequest req)
        {
            return InternalRequestAsync<DescribeDomainListResponse>(req, "DescribeDomainList")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取域名日志
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainLogListRequest"/></param>
        /// <returns><see cref="DescribeDomainLogListResponse"/></returns>
        public Task<DescribeDomainLogListResponse> DescribeDomainLogList(DescribeDomainLogListRequest req)
        {
            return InternalRequestAsync<DescribeDomainLogListResponse>(req, "DescribeDomainLogList");
        }

        /// <summary>
        /// 获取域名日志
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainLogListRequest"/></param>
        /// <returns><see cref="DescribeDomainLogListResponse"/></returns>
        public DescribeDomainLogListResponse DescribeDomainLogListSync(DescribeDomainLogListRequest req)
        {
            return InternalRequestAsync<DescribeDomainLogListResponse>(req, "DescribeDomainLogList")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取域名概览信息
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainPreviewRequest"/></param>
        /// <returns><see cref="DescribeDomainPreviewResponse"/></returns>
        public Task<DescribeDomainPreviewResponse> DescribeDomainPreview(DescribeDomainPreviewRequest req)
        {
            return InternalRequestAsync<DescribeDomainPreviewResponse>(req, "DescribeDomainPreview");
        }

        /// <summary>
        /// 获取域名概览信息
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainPreviewRequest"/></param>
        /// <returns><see cref="DescribeDomainPreviewResponse"/></returns>
        public DescribeDomainPreviewResponse DescribeDomainPreviewSync(DescribeDomainPreviewRequest req)
        {
            return InternalRequestAsync<DescribeDomainPreviewResponse>(req, "DescribeDomainPreview")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取域名权限
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainPurviewRequest"/></param>
        /// <returns><see cref="DescribeDomainPurviewResponse"/></returns>
        public Task<DescribeDomainPurviewResponse> DescribeDomainPurview(DescribeDomainPurviewRequest req)
        {
            return InternalRequestAsync<DescribeDomainPurviewResponse>(req, "DescribeDomainPurview");
        }

        /// <summary>
        /// 获取域名权限
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainPurviewRequest"/></param>
        /// <returns><see cref="DescribeDomainPurviewResponse"/></returns>
        public DescribeDomainPurviewResponse DescribeDomainPurviewSync(DescribeDomainPurviewRequest req)
        {
            return InternalRequestAsync<DescribeDomainPurviewResponse>(req, "DescribeDomainPurview")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取域名共享信息
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainShareInfoRequest"/></param>
        /// <returns><see cref="DescribeDomainShareInfoResponse"/></returns>
        public Task<DescribeDomainShareInfoResponse> DescribeDomainShareInfo(DescribeDomainShareInfoRequest req)
        {
            return InternalRequestAsync<DescribeDomainShareInfoResponse>(req, "DescribeDomainShareInfo");
        }

        /// <summary>
        /// 获取域名共享信息
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainShareInfoRequest"/></param>
        /// <returns><see cref="DescribeDomainShareInfoResponse"/></returns>
        public DescribeDomainShareInfoResponse DescribeDomainShareInfoSync(DescribeDomainShareInfoRequest req)
        {
            return InternalRequestAsync<DescribeDomainShareInfoResponse>(req, "DescribeDomainShareInfo")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取域名Whois信息
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainWhoisRequest"/></param>
        /// <returns><see cref="DescribeDomainWhoisResponse"/></returns>
        public Task<DescribeDomainWhoisResponse> DescribeDomainWhois(DescribeDomainWhoisRequest req)
        {
            return InternalRequestAsync<DescribeDomainWhoisResponse>(req, "DescribeDomainWhois");
        }

        /// <summary>
        /// 获取域名Whois信息
        /// </summary>
        /// <param name="req"><see cref="DescribeDomainWhoisRequest"/></param>
        /// <returns><see cref="DescribeDomainWhoisResponse"/></returns>
        public DescribeDomainWhoisResponse DescribeDomainWhoisSync(DescribeDomainWhoisRequest req)
        {
            return InternalRequestAsync<DescribeDomainWhoisResponse>(req, "DescribeDomainWhois")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取各套餐配置详情
        /// </summary>
        /// <param name="req"><see cref="DescribePackageDetailRequest"/></param>
        /// <returns><see cref="DescribePackageDetailResponse"/></returns>
        public Task<DescribePackageDetailResponse> DescribePackageDetail(DescribePackageDetailRequest req)
        {
            return InternalRequestAsync<DescribePackageDetailResponse>(req, "DescribePackageDetail");
        }

        /// <summary>
        /// 获取各套餐配置详情
        /// </summary>
        /// <param name="req"><see cref="DescribePackageDetailRequest"/></param>
        /// <returns><see cref="DescribePackageDetailResponse"/></returns>
        public DescribePackageDetailResponse DescribePackageDetailSync(DescribePackageDetailRequest req)
        {
            return InternalRequestAsync<DescribePackageDetailResponse>(req, "DescribePackageDetail")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取记录信息
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordRequest"/></param>
        /// <returns><see cref="DescribeRecordResponse"/></returns>
        public Task<DescribeRecordResponse> DescribeRecord(DescribeRecordRequest req)
        {
            return InternalRequestAsync<DescribeRecordResponse>(req, "DescribeRecord");
        }

        /// <summary>
        /// 获取记录信息
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordRequest"/></param>
        /// <returns><see cref="DescribeRecordResponse"/></returns>
        public DescribeRecordResponse DescribeRecordSync(DescribeRecordRequest req)
        {
            return InternalRequestAsync<DescribeRecordResponse>(req, "DescribeRecord")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 判断是否有除系统默认的@-NS记录之外的记录存在
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordExistExceptDefaultNSRequest"/></param>
        /// <returns><see cref="DescribeRecordExistExceptDefaultNSResponse"/></returns>
        public Task<DescribeRecordExistExceptDefaultNSResponse> DescribeRecordExistExceptDefaultNS(DescribeRecordExistExceptDefaultNSRequest req)
        {
            return InternalRequestAsync<DescribeRecordExistExceptDefaultNSResponse>(req, "DescribeRecordExistExceptDefaultNS");
        }

        /// <summary>
        /// 判断是否有除系统默认的@-NS记录之外的记录存在
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordExistExceptDefaultNSRequest"/></param>
        /// <returns><see cref="DescribeRecordExistExceptDefaultNSResponse"/></returns>
        public DescribeRecordExistExceptDefaultNSResponse DescribeRecordExistExceptDefaultNSSync(DescribeRecordExistExceptDefaultNSRequest req)
        {
            return InternalRequestAsync<DescribeRecordExistExceptDefaultNSResponse>(req, "DescribeRecordExistExceptDefaultNS")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取某个域名下的解析记录列表
        /// 备注：
        /// 1. 新添加的解析记录存在短暂的索引延迟，如果查询不到新增记录，请在 30 秒后重试
        /// 2.  API获取的记录总条数会比控制台多2条，原因是： 为了防止用户误操作导致解析服务不可用，对2021-10-29 14:24:26之后添加的域名，在控制台都不显示这2条NS记录。
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordFilterListRequest"/></param>
        /// <returns><see cref="DescribeRecordFilterListResponse"/></returns>
        public Task<DescribeRecordFilterListResponse> DescribeRecordFilterList(DescribeRecordFilterListRequest req)
        {
            return InternalRequestAsync<DescribeRecordFilterListResponse>(req, "DescribeRecordFilterList");
        }

        /// <summary>
        /// 获取某个域名下的解析记录列表
        /// 备注：
        /// 1. 新添加的解析记录存在短暂的索引延迟，如果查询不到新增记录，请在 30 秒后重试
        /// 2.  API获取的记录总条数会比控制台多2条，原因是： 为了防止用户误操作导致解析服务不可用，对2021-10-29 14:24:26之后添加的域名，在控制台都不显示这2条NS记录。
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordFilterListRequest"/></param>
        /// <returns><see cref="DescribeRecordFilterListResponse"/></returns>
        public DescribeRecordFilterListResponse DescribeRecordFilterListSync(DescribeRecordFilterListRequest req)
        {
            return InternalRequestAsync<DescribeRecordFilterListResponse>(req, "DescribeRecordFilterList")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 查询解析记录分组列表
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordGroupListRequest"/></param>
        /// <returns><see cref="DescribeRecordGroupListResponse"/></returns>
        public Task<DescribeRecordGroupListResponse> DescribeRecordGroupList(DescribeRecordGroupListRequest req)
        {
            return InternalRequestAsync<DescribeRecordGroupListResponse>(req, "DescribeRecordGroupList");
        }

        /// <summary>
        /// 查询解析记录分组列表
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordGroupListRequest"/></param>
        /// <returns><see cref="DescribeRecordGroupListResponse"/></returns>
        public DescribeRecordGroupListResponse DescribeRecordGroupListSync(DescribeRecordGroupListRequest req)
        {
            return InternalRequestAsync<DescribeRecordGroupListResponse>(req, "DescribeRecordGroupList")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 按分类返回线路列表
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordLineCategoryListRequest"/></param>
        /// <returns><see cref="DescribeRecordLineCategoryListResponse"/></returns>
        public Task<DescribeRecordLineCategoryListResponse> DescribeRecordLineCategoryList(DescribeRecordLineCategoryListRequest req)
        {
            return InternalRequestAsync<DescribeRecordLineCategoryListResponse>(req, "DescribeRecordLineCategoryList");
        }

        /// <summary>
        /// 按分类返回线路列表
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordLineCategoryListRequest"/></param>
        /// <returns><see cref="DescribeRecordLineCategoryListResponse"/></returns>
        public DescribeRecordLineCategoryListResponse DescribeRecordLineCategoryListSync(DescribeRecordLineCategoryListRequest req)
        {
            return InternalRequestAsync<DescribeRecordLineCategoryListResponse>(req, "DescribeRecordLineCategoryList")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取等级允许的线路
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordLineListRequest"/></param>
        /// <returns><see cref="DescribeRecordLineListResponse"/></returns>
        public Task<DescribeRecordLineListResponse> DescribeRecordLineList(DescribeRecordLineListRequest req)
        {
            return InternalRequestAsync<DescribeRecordLineListResponse>(req, "DescribeRecordLineList");
        }

        /// <summary>
        /// 获取等级允许的线路
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordLineListRequest"/></param>
        /// <returns><see cref="DescribeRecordLineListResponse"/></returns>
        public DescribeRecordLineListResponse DescribeRecordLineListSync(DescribeRecordLineListRequest req)
        {
            return InternalRequestAsync<DescribeRecordLineListResponse>(req, "DescribeRecordLineList")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取某个域名下的解析记录列表
        /// 备注：
        /// 1. 新添加的解析记录存在短暂的索引延迟，如果查询不到新增记录，请在 30 秒后重试
        /// 2.  API获取的记录总条数会比控制台多2条，原因是： 为了防止用户误操作导致解析服务不可用，对2021-10-29 14:24:26之后添加的域名，在控制台都不显示这2条NS记录。
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordListRequest"/></param>
        /// <returns><see cref="DescribeRecordListResponse"/></returns>
        public Task<DescribeRecordListResponse> DescribeRecordList(DescribeRecordListRequest req)
        {
            return InternalRequestAsync<DescribeRecordListResponse>(req, "DescribeRecordList");
        }

        /// <summary>
        /// 获取某个域名下的解析记录列表
        /// 备注：
        /// 1. 新添加的解析记录存在短暂的索引延迟，如果查询不到新增记录，请在 30 秒后重试
        /// 2.  API获取的记录总条数会比控制台多2条，原因是： 为了防止用户误操作导致解析服务不可用，对2021-10-29 14:24:26之后添加的域名，在控制台都不显示这2条NS记录。
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordListRequest"/></param>
        /// <returns><see cref="DescribeRecordListResponse"/></returns>
        public DescribeRecordListResponse DescribeRecordListSync(DescribeRecordListRequest req)
        {
            return InternalRequestAsync<DescribeRecordListResponse>(req, "DescribeRecordList")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 查询解析记录重新回滚的结果
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordSnapshotRollbackResultRequest"/></param>
        /// <returns><see cref="DescribeRecordSnapshotRollbackResultResponse"/></returns>
        public Task<DescribeRecordSnapshotRollbackResultResponse> DescribeRecordSnapshotRollbackResult(DescribeRecordSnapshotRollbackResultRequest req)
        {
            return InternalRequestAsync<DescribeRecordSnapshotRollbackResultResponse>(req, "DescribeRecordSnapshotRollbackResult");
        }

        /// <summary>
        /// 查询解析记录重新回滚的结果
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordSnapshotRollbackResultRequest"/></param>
        /// <returns><see cref="DescribeRecordSnapshotRollbackResultResponse"/></returns>
        public DescribeRecordSnapshotRollbackResultResponse DescribeRecordSnapshotRollbackResultSync(DescribeRecordSnapshotRollbackResultRequest req)
        {
            return InternalRequestAsync<DescribeRecordSnapshotRollbackResultResponse>(req, "DescribeRecordSnapshotRollbackResult")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取等级允许的记录类型
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordTypeRequest"/></param>
        /// <returns><see cref="DescribeRecordTypeResponse"/></returns>
        public Task<DescribeRecordTypeResponse> DescribeRecordType(DescribeRecordTypeRequest req)
        {
            return InternalRequestAsync<DescribeRecordTypeResponse>(req, "DescribeRecordType");
        }

        /// <summary>
        /// 获取等级允许的记录类型
        /// </summary>
        /// <param name="req"><see cref="DescribeRecordTypeRequest"/></param>
        /// <returns><see cref="DescribeRecordTypeResponse"/></returns>
        public DescribeRecordTypeResponse DescribeRecordTypeSync(DescribeRecordTypeRequest req)
        {
            return InternalRequestAsync<DescribeRecordTypeResponse>(req, "DescribeRecordType")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 查询解析快照配置
        /// </summary>
        /// <param name="req"><see cref="DescribeSnapshotConfigRequest"/></param>
        /// <returns><see cref="DescribeSnapshotConfigResponse"/></returns>
        public Task<DescribeSnapshotConfigResponse> DescribeSnapshotConfig(DescribeSnapshotConfigRequest req)
        {
            return InternalRequestAsync<DescribeSnapshotConfigResponse>(req, "DescribeSnapshotConfig");
        }

        /// <summary>
        /// 查询解析快照配置
        /// </summary>
        /// <param name="req"><see cref="DescribeSnapshotConfigRequest"/></param>
        /// <returns><see cref="DescribeSnapshotConfigResponse"/></returns>
        public DescribeSnapshotConfigResponse DescribeSnapshotConfigSync(DescribeSnapshotConfigRequest req)
        {
            return InternalRequestAsync<DescribeSnapshotConfigResponse>(req, "DescribeSnapshotConfig")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 查询快照列表
        /// </summary>
        /// <param name="req"><see cref="DescribeSnapshotListRequest"/></param>
        /// <returns><see cref="DescribeSnapshotListResponse"/></returns>
        public Task<DescribeSnapshotListResponse> DescribeSnapshotList(DescribeSnapshotListRequest req)
        {
            return InternalRequestAsync<DescribeSnapshotListResponse>(req, "DescribeSnapshotList");
        }

        /// <summary>
        /// 查询快照列表
        /// </summary>
        /// <param name="req"><see cref="DescribeSnapshotListRequest"/></param>
        /// <returns><see cref="DescribeSnapshotListResponse"/></returns>
        public DescribeSnapshotListResponse DescribeSnapshotListSync(DescribeSnapshotListRequest req)
        {
            return InternalRequestAsync<DescribeSnapshotListResponse>(req, "DescribeSnapshotList")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 查询快照回滚结果
        /// </summary>
        /// <param name="req"><see cref="DescribeSnapshotRollbackResultRequest"/></param>
        /// <returns><see cref="DescribeSnapshotRollbackResultResponse"/></returns>
        public Task<DescribeSnapshotRollbackResultResponse> DescribeSnapshotRollbackResult(DescribeSnapshotRollbackResultRequest req)
        {
            return InternalRequestAsync<DescribeSnapshotRollbackResultResponse>(req, "DescribeSnapshotRollbackResult");
        }

        /// <summary>
        /// 查询快照回滚结果
        /// </summary>
        /// <param name="req"><see cref="DescribeSnapshotRollbackResultRequest"/></param>
        /// <returns><see cref="DescribeSnapshotRollbackResultResponse"/></returns>
        public DescribeSnapshotRollbackResultResponse DescribeSnapshotRollbackResultSync(DescribeSnapshotRollbackResultRequest req)
        {
            return InternalRequestAsync<DescribeSnapshotRollbackResultResponse>(req, "DescribeSnapshotRollbackResult")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 查询最近一次回滚
        /// </summary>
        /// <param name="req"><see cref="DescribeSnapshotRollbackTaskRequest"/></param>
        /// <returns><see cref="DescribeSnapshotRollbackTaskResponse"/></returns>
        public Task<DescribeSnapshotRollbackTaskResponse> DescribeSnapshotRollbackTask(DescribeSnapshotRollbackTaskRequest req)
        {
            return InternalRequestAsync<DescribeSnapshotRollbackTaskResponse>(req, "DescribeSnapshotRollbackTask");
        }

        /// <summary>
        /// 查询最近一次回滚
        /// </summary>
        /// <param name="req"><see cref="DescribeSnapshotRollbackTaskRequest"/></param>
        /// <returns><see cref="DescribeSnapshotRollbackTaskResponse"/></returns>
        public DescribeSnapshotRollbackTaskResponse DescribeSnapshotRollbackTaskSync(DescribeSnapshotRollbackTaskRequest req)
        {
            return InternalRequestAsync<DescribeSnapshotRollbackTaskResponse>(req, "DescribeSnapshotRollbackTask")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 统计子域名的解析量，帮助您了解流量情况、时间段分布。支持查看近 3 个月内的统计情况。仅付费套餐域名可用。
        /// </summary>
        /// <param name="req"><see cref="DescribeSubdomainAnalyticsRequest"/></param>
        /// <returns><see cref="DescribeSubdomainAnalyticsResponse"/></returns>
        public Task<DescribeSubdomainAnalyticsResponse> DescribeSubdomainAnalytics(DescribeSubdomainAnalyticsRequest req)
        {
            return InternalRequestAsync<DescribeSubdomainAnalyticsResponse>(req, "DescribeSubdomainAnalytics");
        }

        /// <summary>
        /// 统计子域名的解析量，帮助您了解流量情况、时间段分布。支持查看近 3 个月内的统计情况。仅付费套餐域名可用。
        /// </summary>
        /// <param name="req"><see cref="DescribeSubdomainAnalyticsRequest"/></param>
        /// <returns><see cref="DescribeSubdomainAnalyticsResponse"/></returns>
        public DescribeSubdomainAnalyticsResponse DescribeSubdomainAnalyticsSync(DescribeSubdomainAnalyticsRequest req)
        {
            return InternalRequestAsync<DescribeSubdomainAnalyticsResponse>(req, "DescribeSubdomainAnalytics")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取账户信息
        /// </summary>
        /// <param name="req"><see cref="DescribeUserDetailRequest"/></param>
        /// <returns><see cref="DescribeUserDetailResponse"/></returns>
        public Task<DescribeUserDetailResponse> DescribeUserDetail(DescribeUserDetailRequest req)
        {
            return InternalRequestAsync<DescribeUserDetailResponse>(req, "DescribeUserDetail");
        }

        /// <summary>
        /// 获取账户信息
        /// </summary>
        /// <param name="req"><see cref="DescribeUserDetailRequest"/></param>
        /// <returns><see cref="DescribeUserDetailResponse"/></returns>
        public DescribeUserDetailResponse DescribeUserDetailSync(DescribeUserDetailRequest req)
        {
            return InternalRequestAsync<DescribeUserDetailResponse>(req, "DescribeUserDetail")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取域名增值服务用量
        /// </summary>
        /// <param name="req"><see cref="DescribeVASStatisticRequest"/></param>
        /// <returns><see cref="DescribeVASStatisticResponse"/></returns>
        public Task<DescribeVASStatisticResponse> DescribeVASStatistic(DescribeVASStatisticRequest req)
        {
            return InternalRequestAsync<DescribeVASStatisticResponse>(req, "DescribeVASStatistic");
        }

        /// <summary>
        /// 获取域名增值服务用量
        /// </summary>
        /// <param name="req"><see cref="DescribeVASStatisticRequest"/></param>
        /// <returns><see cref="DescribeVASStatisticResponse"/></returns>
        public DescribeVASStatisticResponse DescribeVASStatisticSync(DescribeVASStatisticRequest req)
        {
            return InternalRequestAsync<DescribeVASStatisticResponse>(req, "DescribeVASStatistic")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 下载快照
        /// </summary>
        /// <param name="req"><see cref="DownloadSnapshotRequest"/></param>
        /// <returns><see cref="DownloadSnapshotResponse"/></returns>
        public Task<DownloadSnapshotResponse> DownloadSnapshot(DownloadSnapshotRequest req)
        {
            return InternalRequestAsync<DownloadSnapshotResponse>(req, "DownloadSnapshot");
        }

        /// <summary>
        /// 下载快照
        /// </summary>
        /// <param name="req"><see cref="DownloadSnapshotRequest"/></param>
        /// <returns><see cref="DownloadSnapshotResponse"/></returns>
        public DownloadSnapshotResponse DownloadSnapshotSync(DownloadSnapshotRequest req)
        {
            return InternalRequestAsync<DownloadSnapshotResponse>(req, "DownloadSnapshot")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 修改域名的自定义线路
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainCustomLineRequest"/></param>
        /// <returns><see cref="ModifyDomainCustomLineResponse"/></returns>
        public Task<ModifyDomainCustomLineResponse> ModifyDomainCustomLine(ModifyDomainCustomLineRequest req)
        {
            return InternalRequestAsync<ModifyDomainCustomLineResponse>(req, "ModifyDomainCustomLine");
        }

        /// <summary>
        /// 修改域名的自定义线路
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainCustomLineRequest"/></param>
        /// <returns><see cref="ModifyDomainCustomLineResponse"/></returns>
        public ModifyDomainCustomLineResponse ModifyDomainCustomLineSync(ModifyDomainCustomLineRequest req)
        {
            return InternalRequestAsync<ModifyDomainCustomLineResponse>(req, "ModifyDomainCustomLine")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 锁定域名
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainLockRequest"/></param>
        /// <returns><see cref="ModifyDomainLockResponse"/></returns>
        public Task<ModifyDomainLockResponse> ModifyDomainLock(ModifyDomainLockRequest req)
        {
            return InternalRequestAsync<ModifyDomainLockResponse>(req, "ModifyDomainLock");
        }

        /// <summary>
        /// 锁定域名
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainLockRequest"/></param>
        /// <returns><see cref="ModifyDomainLockResponse"/></returns>
        public ModifyDomainLockResponse ModifyDomainLockSync(ModifyDomainLockRequest req)
        {
            return InternalRequestAsync<ModifyDomainLockResponse>(req, "ModifyDomainLock")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 域名过户
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainOwnerRequest"/></param>
        /// <returns><see cref="ModifyDomainOwnerResponse"/></returns>
        public Task<ModifyDomainOwnerResponse> ModifyDomainOwner(ModifyDomainOwnerRequest req)
        {
            return InternalRequestAsync<ModifyDomainOwnerResponse>(req, "ModifyDomainOwner");
        }

        /// <summary>
        /// 域名过户
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainOwnerRequest"/></param>
        /// <returns><see cref="ModifyDomainOwnerResponse"/></returns>
        public ModifyDomainOwnerResponse ModifyDomainOwnerSync(ModifyDomainOwnerRequest req)
        {
            return InternalRequestAsync<ModifyDomainOwnerResponse>(req, "ModifyDomainOwner")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 设置域名备注
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainRemarkRequest"/></param>
        /// <returns><see cref="ModifyDomainRemarkResponse"/></returns>
        public Task<ModifyDomainRemarkResponse> ModifyDomainRemark(ModifyDomainRemarkRequest req)
        {
            return InternalRequestAsync<ModifyDomainRemarkResponse>(req, "ModifyDomainRemark");
        }

        /// <summary>
        /// 设置域名备注
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainRemarkRequest"/></param>
        /// <returns><see cref="ModifyDomainRemarkResponse"/></returns>
        public ModifyDomainRemarkResponse ModifyDomainRemarkSync(ModifyDomainRemarkRequest req)
        {
            return InternalRequestAsync<ModifyDomainRemarkResponse>(req, "ModifyDomainRemark")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 修改域名状态
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainStatusRequest"/></param>
        /// <returns><see cref="ModifyDomainStatusResponse"/></returns>
        public Task<ModifyDomainStatusResponse> ModifyDomainStatus(ModifyDomainStatusRequest req)
        {
            return InternalRequestAsync<ModifyDomainStatusResponse>(req, "ModifyDomainStatus");
        }

        /// <summary>
        /// 修改域名状态
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainStatusRequest"/></param>
        /// <returns><see cref="ModifyDomainStatusResponse"/></returns>
        public ModifyDomainStatusResponse ModifyDomainStatusSync(ModifyDomainStatusRequest req)
        {
            return InternalRequestAsync<ModifyDomainStatusResponse>(req, "ModifyDomainStatus")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 修改域名所属分组
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainToGroupRequest"/></param>
        /// <returns><see cref="ModifyDomainToGroupResponse"/></returns>
        public Task<ModifyDomainToGroupResponse> ModifyDomainToGroup(ModifyDomainToGroupRequest req)
        {
            return InternalRequestAsync<ModifyDomainToGroupResponse>(req, "ModifyDomainToGroup");
        }

        /// <summary>
        /// 修改域名所属分组
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainToGroupRequest"/></param>
        /// <returns><see cref="ModifyDomainToGroupResponse"/></returns>
        public ModifyDomainToGroupResponse ModifyDomainToGroupSync(ModifyDomainToGroupRequest req)
        {
            return InternalRequestAsync<ModifyDomainToGroupResponse>(req, "ModifyDomainToGroup")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 域名锁定解锁
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainUnlockRequest"/></param>
        /// <returns><see cref="ModifyDomainUnlockResponse"/></returns>
        public Task<ModifyDomainUnlockResponse> ModifyDomainUnlock(ModifyDomainUnlockRequest req)
        {
            return InternalRequestAsync<ModifyDomainUnlockResponse>(req, "ModifyDomainUnlock");
        }

        /// <summary>
        /// 域名锁定解锁
        /// </summary>
        /// <param name="req"><see cref="ModifyDomainUnlockRequest"/></param>
        /// <returns><see cref="ModifyDomainUnlockResponse"/></returns>
        public ModifyDomainUnlockResponse ModifyDomainUnlockSync(ModifyDomainUnlockRequest req)
        {
            return InternalRequestAsync<ModifyDomainUnlockResponse>(req, "ModifyDomainUnlock")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 更新动态 DNS 记录
        /// </summary>
        /// <param name="req"><see cref="ModifyDynamicDNSRequest"/></param>
        /// <returns><see cref="ModifyDynamicDNSResponse"/></returns>
        public Task<ModifyDynamicDNSResponse> ModifyDynamicDNS(ModifyDynamicDNSRequest req)
        {
            return InternalRequestAsync<ModifyDynamicDNSResponse>(req, "ModifyDynamicDNS");
        }

        /// <summary>
        /// 更新动态 DNS 记录
        /// </summary>
        /// <param name="req"><see cref="ModifyDynamicDNSRequest"/></param>
        /// <returns><see cref="ModifyDynamicDNSResponse"/></returns>
        public ModifyDynamicDNSResponse ModifyDynamicDNSSync(ModifyDynamicDNSRequest req)
        {
            return InternalRequestAsync<ModifyDynamicDNSResponse>(req, "ModifyDynamicDNS")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// DNS 解析套餐自动续费设置
        /// </summary>
        /// <param name="req"><see cref="ModifyPackageAutoRenewRequest"/></param>
        /// <returns><see cref="ModifyPackageAutoRenewResponse"/></returns>
        public Task<ModifyPackageAutoRenewResponse> ModifyPackageAutoRenew(ModifyPackageAutoRenewRequest req)
        {
            return InternalRequestAsync<ModifyPackageAutoRenewResponse>(req, "ModifyPackageAutoRenew");
        }

        /// <summary>
        /// DNS 解析套餐自动续费设置
        /// </summary>
        /// <param name="req"><see cref="ModifyPackageAutoRenewRequest"/></param>
        /// <returns><see cref="ModifyPackageAutoRenewResponse"/></returns>
        public ModifyPackageAutoRenewResponse ModifyPackageAutoRenewSync(ModifyPackageAutoRenewRequest req)
        {
            return InternalRequestAsync<ModifyPackageAutoRenewResponse>(req, "ModifyPackageAutoRenew")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 修改记录
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordRequest"/></param>
        /// <returns><see cref="ModifyRecordResponse"/></returns>
        public Task<ModifyRecordResponse> ModifyRecord(ModifyRecordRequest req)
        {
            return InternalRequestAsync<ModifyRecordResponse>(req, "ModifyRecord");
        }

        /// <summary>
        /// 修改记录
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordRequest"/></param>
        /// <returns><see cref="ModifyRecordResponse"/></returns>
        public ModifyRecordResponse ModifyRecordSync(ModifyRecordRequest req)
        {
            return InternalRequestAsync<ModifyRecordResponse>(req, "ModifyRecord")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 批量修改记录
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordBatchRequest"/></param>
        /// <returns><see cref="ModifyRecordBatchResponse"/></returns>
        public Task<ModifyRecordBatchResponse> ModifyRecordBatch(ModifyRecordBatchRequest req)
        {
            return InternalRequestAsync<ModifyRecordBatchResponse>(req, "ModifyRecordBatch");
        }

        /// <summary>
        /// 批量修改记录
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordBatchRequest"/></param>
        /// <returns><see cref="ModifyRecordBatchResponse"/></returns>
        public ModifyRecordBatchResponse ModifyRecordBatchSync(ModifyRecordBatchRequest req)
        {
            return InternalRequestAsync<ModifyRecordBatchResponse>(req, "ModifyRecordBatch")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 修改记录可选字段
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordFieldsRequest"/></param>
        /// <returns><see cref="ModifyRecordFieldsResponse"/></returns>
        public Task<ModifyRecordFieldsResponse> ModifyRecordFields(ModifyRecordFieldsRequest req)
        {
            return InternalRequestAsync<ModifyRecordFieldsResponse>(req, "ModifyRecordFields");
        }

        /// <summary>
        /// 修改记录可选字段
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordFieldsRequest"/></param>
        /// <returns><see cref="ModifyRecordFieldsResponse"/></returns>
        public ModifyRecordFieldsResponse ModifyRecordFieldsSync(ModifyRecordFieldsRequest req)
        {
            return InternalRequestAsync<ModifyRecordFieldsResponse>(req, "ModifyRecordFields")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 修改记录分组
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordGroupRequest"/></param>
        /// <returns><see cref="ModifyRecordGroupResponse"/></returns>
        public Task<ModifyRecordGroupResponse> ModifyRecordGroup(ModifyRecordGroupRequest req)
        {
            return InternalRequestAsync<ModifyRecordGroupResponse>(req, "ModifyRecordGroup");
        }

        /// <summary>
        /// 修改记录分组
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordGroupRequest"/></param>
        /// <returns><see cref="ModifyRecordGroupResponse"/></returns>
        public ModifyRecordGroupResponse ModifyRecordGroupSync(ModifyRecordGroupRequest req)
        {
            return InternalRequestAsync<ModifyRecordGroupResponse>(req, "ModifyRecordGroup")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 设置记录备注
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordRemarkRequest"/></param>
        /// <returns><see cref="ModifyRecordRemarkResponse"/></returns>
        public Task<ModifyRecordRemarkResponse> ModifyRecordRemark(ModifyRecordRemarkRequest req)
        {
            return InternalRequestAsync<ModifyRecordRemarkResponse>(req, "ModifyRecordRemark");
        }

        /// <summary>
        /// 设置记录备注
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordRemarkRequest"/></param>
        /// <returns><see cref="ModifyRecordRemarkResponse"/></returns>
        public ModifyRecordRemarkResponse ModifyRecordRemarkSync(ModifyRecordRemarkRequest req)
        {
            return InternalRequestAsync<ModifyRecordRemarkResponse>(req, "ModifyRecordRemark")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 修改解析记录的状态
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordStatusRequest"/></param>
        /// <returns><see cref="ModifyRecordStatusResponse"/></returns>
        public Task<ModifyRecordStatusResponse> ModifyRecordStatus(ModifyRecordStatusRequest req)
        {
            return InternalRequestAsync<ModifyRecordStatusResponse>(req, "ModifyRecordStatus");
        }

        /// <summary>
        /// 修改解析记录的状态
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordStatusRequest"/></param>
        /// <returns><see cref="ModifyRecordStatusResponse"/></returns>
        public ModifyRecordStatusResponse ModifyRecordStatusSync(ModifyRecordStatusRequest req)
        {
            return InternalRequestAsync<ModifyRecordStatusResponse>(req, "ModifyRecordStatus")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 将记录添加到分组
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordToGroupRequest"/></param>
        /// <returns><see cref="ModifyRecordToGroupResponse"/></returns>
        public Task<ModifyRecordToGroupResponse> ModifyRecordToGroup(ModifyRecordToGroupRequest req)
        {
            return InternalRequestAsync<ModifyRecordToGroupResponse>(req, "ModifyRecordToGroup");
        }

        /// <summary>
        /// 将记录添加到分组
        /// </summary>
        /// <param name="req"><see cref="ModifyRecordToGroupRequest"/></param>
        /// <returns><see cref="ModifyRecordToGroupResponse"/></returns>
        public ModifyRecordToGroupResponse ModifyRecordToGroupSync(ModifyRecordToGroupRequest req)
        {
            return InternalRequestAsync<ModifyRecordToGroupResponse>(req, "ModifyRecordToGroup")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 修改快照配置
        /// </summary>
        /// <param name="req"><see cref="ModifySnapshotConfigRequest"/></param>
        /// <returns><see cref="ModifySnapshotConfigResponse"/></returns>
        public Task<ModifySnapshotConfigResponse> ModifySnapshotConfig(ModifySnapshotConfigRequest req)
        {
            return InternalRequestAsync<ModifySnapshotConfigResponse>(req, "ModifySnapshotConfig");
        }

        /// <summary>
        /// 修改快照配置
        /// </summary>
        /// <param name="req"><see cref="ModifySnapshotConfigRequest"/></param>
        /// <returns><see cref="ModifySnapshotConfigResponse"/></returns>
        public ModifySnapshotConfigResponse ModifySnapshotConfigSync(ModifySnapshotConfigRequest req)
        {
            return InternalRequestAsync<ModifySnapshotConfigResponse>(req, "ModifySnapshotConfig")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 暂停子域名的解析记录
        /// </summary>
        /// <param name="req"><see cref="ModifySubdomainStatusRequest"/></param>
        /// <returns><see cref="ModifySubdomainStatusResponse"/></returns>
        public Task<ModifySubdomainStatusResponse> ModifySubdomainStatus(ModifySubdomainStatusRequest req)
        {
            return InternalRequestAsync<ModifySubdomainStatusResponse>(req, "ModifySubdomainStatus");
        }

        /// <summary>
        /// 暂停子域名的解析记录
        /// </summary>
        /// <param name="req"><see cref="ModifySubdomainStatusRequest"/></param>
        /// <returns><see cref="ModifySubdomainStatusResponse"/></returns>
        public ModifySubdomainStatusResponse ModifySubdomainStatusSync(ModifySubdomainStatusRequest req)
        {
            return InternalRequestAsync<ModifySubdomainStatusResponse>(req, "ModifySubdomainStatus")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 增值服务自动续费设置
        /// </summary>
        /// <param name="req"><see cref="ModifyVasAutoRenewStatusRequest"/></param>
        /// <returns><see cref="ModifyVasAutoRenewStatusResponse"/></returns>
        public Task<ModifyVasAutoRenewStatusResponse> ModifyVasAutoRenewStatus(ModifyVasAutoRenewStatusRequest req)
        {
            return InternalRequestAsync<ModifyVasAutoRenewStatusResponse>(req, "ModifyVasAutoRenewStatus");
        }

        /// <summary>
        /// 增值服务自动续费设置
        /// </summary>
        /// <param name="req"><see cref="ModifyVasAutoRenewStatusRequest"/></param>
        /// <returns><see cref="ModifyVasAutoRenewStatusResponse"/></returns>
        public ModifyVasAutoRenewStatusResponse ModifyVasAutoRenewStatusSync(ModifyVasAutoRenewStatusRequest req)
        {
            return InternalRequestAsync<ModifyVasAutoRenewStatusResponse>(req, "ModifyVasAutoRenewStatus")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// DNSPod商品余额支付
        /// </summary>
        /// <param name="req"><see cref="PayOrderWithBalanceRequest"/></param>
        /// <returns><see cref="PayOrderWithBalanceResponse"/></returns>
        public Task<PayOrderWithBalanceResponse> PayOrderWithBalance(PayOrderWithBalanceRequest req)
        {
            return InternalRequestAsync<PayOrderWithBalanceResponse>(req, "PayOrderWithBalance");
        }

        /// <summary>
        /// DNSPod商品余额支付
        /// </summary>
        /// <param name="req"><see cref="PayOrderWithBalanceRequest"/></param>
        /// <returns><see cref="PayOrderWithBalanceResponse"/></returns>
        public PayOrderWithBalanceResponse PayOrderWithBalanceSync(PayOrderWithBalanceRequest req)
        {
            return InternalRequestAsync<PayOrderWithBalanceResponse>(req, "PayOrderWithBalance")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 重新回滚指定解析记录快照
        /// </summary>
        /// <param name="req"><see cref="RollbackRecordSnapshotRequest"/></param>
        /// <returns><see cref="RollbackRecordSnapshotResponse"/></returns>
        public Task<RollbackRecordSnapshotResponse> RollbackRecordSnapshot(RollbackRecordSnapshotRequest req)
        {
            return InternalRequestAsync<RollbackRecordSnapshotResponse>(req, "RollbackRecordSnapshot");
        }

        /// <summary>
        /// 重新回滚指定解析记录快照
        /// </summary>
        /// <param name="req"><see cref="RollbackRecordSnapshotRequest"/></param>
        /// <returns><see cref="RollbackRecordSnapshotResponse"/></returns>
        public RollbackRecordSnapshotResponse RollbackRecordSnapshotSync(RollbackRecordSnapshotRequest req)
        {
            return InternalRequestAsync<RollbackRecordSnapshotResponse>(req, "RollbackRecordSnapshot")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 回滚快照
        /// </summary>
        /// <param name="req"><see cref="RollbackSnapshotRequest"/></param>
        /// <returns><see cref="RollbackSnapshotResponse"/></returns>
        public Task<RollbackSnapshotResponse> RollbackSnapshot(RollbackSnapshotRequest req)
        {
            return InternalRequestAsync<RollbackSnapshotResponse>(req, "RollbackSnapshot");
        }

        /// <summary>
        /// 回滚快照
        /// </summary>
        /// <param name="req"><see cref="RollbackSnapshotRequest"/></param>
        /// <returns><see cref="RollbackSnapshotResponse"/></returns>
        public RollbackSnapshotResponse RollbackSnapshotSync(RollbackSnapshotRequest req)
        {
            return InternalRequestAsync<RollbackSnapshotResponse>(req, "RollbackSnapshot")
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

    }
}
