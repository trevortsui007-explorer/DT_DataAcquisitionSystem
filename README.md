# DT_DataAcquisitionSystem (DT-DAS)

[![Platform](https://img.shields.io/badge/.NET-6.0%2B-blue.svg)](https://dotnet.microsoft.com/)
[![Architecture](https://img.shields.io/badge/Architecture-DDD%20%2F%20Clean-green.svg)](#架构深度解析)
[![License](https://img.shields.io/badge/License-MIT-important.svg)](LICENSE)

## 📖 项目简介
DT-DAS 是一套面向工业 4.0 环境设计的**高性能、插拔式数据采集与处理系统**。系统通过高度抽象的“提供者-解析器-处理器”链路，解决了异构数据源（FTP、SMB、本地、数据库）接入难、处理逻辑耦合重、采集状态难追踪的问题。

---

## 🚀 核心技术亮点

### 1. 插件化基础设施 (Pluggable Infrastructure)
- **多协议接入**: 抽象 `IFileProvider` 接口，内置 `Local` 与 `Ftp` 实现。通过工厂模式实现“协议热插拔”。
- **流式解析引擎**: 基于 `BaseStreamParser` 的高性能解析链，支持 `CSV` 和 `Excel`。采用**流式读取**而非全载入内存，极大地降低了超大文件的 OOM 风险。

### 2. 响应式流水线 (Reactive Pipeline)
- **后期处理链**: 支持 `IPostProcessor` 机制。在数据入库后，可自动触发如 `MasonETFailureProcessor` 的业务规则校验，实现采集与业务逻辑的彻底解耦。

### 3. 企业级任务管理
- **ACID 状态机**: 通过 `AcquisitionTask` 维护采集作业的生命周期，支持断点续传与失败回溯。
- **IoC 自动化注入**: 深度定制 `UnityJobActivator` 与多种 `IocHelper`，实现复杂服务关系的自动装配。

---

## 🏗 架构深度解析

本项目严格遵循 **Clean Architecture (整洁架构)** 思想，确保核心业务逻辑与外部技术栈隔离：

| 层次 | 核心职责 | 包含组件 |
| :--- | :--- | :--- |
| **WebApi** | 外部网关 & 通讯 | 控制器、鉴权中间件、Swagger 文档。 |
| **Application** | 业务用例编排 | `DataAcquisitionService` (主流程控制)、`FileDiscovery` (扫描算法)、`PostProcessors`。 |
| **Domain** | 业务心脏 & 契约 | 聚合根 (`AcquisitionConfig`)、状态枚举、各层通用接口定义 (`IRepository`)。 |
| **Infrastructure** | 技术实现细节 | 数据库持久化、第三方协议客户端 (FTP)、特定格式解析引擎。 |
| **Common** | 横切关注点 | 内存集合扩展 (`ConcurrentHashSet`)、IoC 辅助工具、时间工具类。 |

---

## 📂 项目结构概览

```plaintext
.
├── WebApi            # 暴露 RESTful 接口，处理 HTTP 请求
├── Application       # 实现具体的采集业务逻辑流，不涉及 SQL 或 协议细节
├── Domain            # 定义业务模型与核心抽象契约（最内层，无外部依赖）
├── Infrastructure    # 具体技术实现：EF/SqlData、FTPClient、ExcelParser
└── Common            # 跨层级的工具类与框架扩展
```

---
## 🛠 扩展指南：如何添加一个新的采集点？

1. **定义配置**: 在 `DA_AcquisitionConfig` 下扩展配置模型。
    
2. **实现解析器**: 若是新格式，在 `Infrastructure/Parsers` 下继承 `BaseStreamParser`。
    
3. **注册 IoC**: 在对应的 `IocHelper` 中添加新类型的映射。
    
4. **注入业务**: 实现 `IPostProcessor` 编写特定的清洗逻辑。
---
## ⚙️ 快速部署
```bash
# 克隆仓库 
git clone [https://github.com/trevortsui007-explorer/DT_DataAcquisitionSystem.git](https://github.com/trevortsui007-explorer/DT_DataAcquisitionSystem.git)
```
```
# 使用VS打开项目，运行WebApi程序，即可调用接口
# Hangfire部分在Startup.cs中，启动时会自动挂载`DA_AcquisitionConfig`下的配置任务
```

---
## 🧪 接口调试与测试
本项目配套完整的 Postman 测试集合：
1. **获取脚本**: 见目录 `Tests/Postman/`。
2. **快速导入**: 打开 Postman -> Import -> 选择 `DT_DAS_Collection.json`。
3. **环境变量**: 确保 `baseUrl` 指向你的本地运行地址（默认 `localhost:40743 or...`）。