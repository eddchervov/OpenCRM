<a name="notification-service-configuration"></a>

# 通知服务配置

[English](README.md) | [Русский](README.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

配置单个通知服务流程,通过环境变量和从寄存器中排除的本地配置.

<a name="branding"></a>

## 品牌

模板、电子邮件主题和 `FromName` 来源于共享 `Helpers.ProjectBrand`: 它读作: `configuration/brand.json`,链接到输出目录中作为 `brand.json`. 密码 `Brand__DisplayName` 和 `Brand__PublicBaseUrl` 环境变量凌驾于此过程中的值。

更改共享后 `brand.json`,重建服务或在"工人"发射目录中安装一致的文件版本.

<a name="providers-and-sender"></a>

## 供应商和发送人

 通过环境变量分别配置真正的提供者(SMTP和信使),访问密钥,以及电子邮件发送地址. 电子邮件默认被禁用,需要明确的配置才能启用.  别把秘密放进去 `brand.json` 或寄存器; 使用环境变量进行本地开发。 通知项目本身 `dotnet user-secrets`  未链接到共享的WebApp标识符。

更改设置后重新启动受影响的进程 。
