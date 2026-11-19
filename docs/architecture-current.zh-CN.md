<a name="application-architecture"></a>

# 应用架构

[English](architecture-current.md) | [Русский](architecture-current.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

 该解决方案将网络主机,业务模块,客户端应用程序,以及背景服务分开.

<a name="main-components"></a>

## 主要组成部分

- `src/OpenCRM.WebApp` ——中性可执行文件,配置,资源.
- `src/OpenCRM.Hosting` ——共享网络组成;从WebApp源树上编译通用控制器和Razor视图,供中和出品主机使用.
- `src/OpenCRM.Modules.*` 业务单元及其合同。
- `src/OpenCRM.Infrastructure.*` 进入数据库、外部服务和环境。
- `src/OpenCRM.Frontend` ——客户端应用程序,管理界面,以及共享的UI软件包.
- `services/notifications` 和 `services/payments` 单独提供服务,提供自己的解决方案和配置。
- `tests` 和 `services/*/tests` ——试验项目.

.NET的主要解决方案是 `OpenCRM.sln`.  每个单独的服务可能都有自己的解决方案,启动指令,以及配置参数.

<a name="changing-components"></a>

## 改变组件

 在改变跨模块互动之前,检查 `*.Contracts` 和现有的抚养登记。  通过本地配置或环境变量提供连接设置;在源文件中不使用硬码秘密或部署特定数据.

模块责任,实际关系,以及配置参数由代码和项目文件来定义. 相邻目录中的UML图具有说明性,应与相应的架构变化一起更新.
