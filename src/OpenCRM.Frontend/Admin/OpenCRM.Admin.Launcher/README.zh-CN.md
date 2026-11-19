<a name="opencrmadminlauncher"></a>

# OpenCRM.Admin. 劳恩彻

[English](README.md) | [Русский](README.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

 这个Windows应用程序在应用模式中通过Google Chrome打开了现有的Vue admin接口,没有制表符或地址栏. 表格、API、认证和商业逻辑仍然与网络应用程序共享。

面板 URL 来源于共享 `brand.json` 复制到应用程序目录; `OPENCRM_ADMIN_API_URL` 环境变量超过它。 默认是来自 `brand.json`.  对于本地开发,设置基础 URL :

```powershell
$env:OPENCRM_ADMIN_API_URL = 'https://localhost:7142/'
```

饼干被储存在 `%LOCALAPPDATA%\Администрирование\AdminDesktop\Chrome` 所以每次发射都不必再签了 服务器继续针对每个请求执行角色和权限.

<a name="browser-behavior"></a>

## 浏览器行为

颜色从 `--disable-http2` 和 `--disable-quic`.  这些浏览器旗号不会改变服务器协议设置 。
Chrome重新使用其单独配置的已存在进程。  在改变发射参数后,只关闭 Chrome 实例 `%LOCALAPPDATA%\Администрирование\AdminDesktop\Chrome` 并重新启动应用程序。  不关闭用户的普通 Chrome 实例 。

验证实际加载页面的启动, 以及包含工程显示名称或“ @%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%% 任务栏窗口名称是 `<display name> — Администрирование`. 应用程序日志为 `%LOCALAPPDATA%\OpenCRM\AdminDesktop\Logs\application.log`.

<a name="build-for-a-product-installation"></a>

## 建造产品安装装置

同样的桌面来源可以在不改变核心配置的情况下使用消费者的品牌文件. 绝对通过 `ProjectBrandFile` 用于构建/打印的路径; 将其复制到输出为 `brand.json`.  没有财产,共享的 `configuration/brand.json` 已使用。 相应的公共基础URL决定了API地址; `OPENCRM_ADMIN_API_URL` 可以在运行时覆盖地址 。

```powershell
dotnet publish --configuration Release -p:ProjectBrandFile="C:/work/Product/brand.json"
```

 从此工程的目录中运行并替换示例路径 。 使由此产生的配置与消费者的网络主机保持一致.  此更改配置包装; 它不会重复行政用户界面或禁用服务器侧访问检查 。
