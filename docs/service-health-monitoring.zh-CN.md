<a name="service-readiness-checks"></a>

# 服务健康检查

[English](service-health-monitoring.md) | [Русский](service-health-monitoring.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

付款和通知服务暴露了HTTP内部的终点:

| 终点 | 目的 |
| --- | --- |
| `/health/live` | 进程响应; 不检查依赖性 。 |
| `/health/ready` | 检查服务配置的依赖性, 包括 PostgreSQL 和信件总线 。 |

 一个成功的检查返回了 HTTP 200 。 退化和不健康的州归还HTTP 503。 准备状态并不证实发送了特定的电子邮件、付款处理或每一种业务情景,而是分别加以验证。

网络应用程序曝光 `/api/health/ready` 以检查其数据库连接。 服务 `--migrate` 模式在启动 HTTP 处理器之前应用迁移和退出。

部署所有者决定安装的URL,代理,请求限制,监控,以及提醒接收者.  只披露您需要的支票, 并且不对外披露连接字符串、 秘密或例外细节 。 核心不包括特定服务器的现成配置.
