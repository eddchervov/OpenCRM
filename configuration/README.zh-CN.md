<a name="configuration"></a>

# 品牌配置

[English](README.md) | [Русский](README.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

 此目录包含共享 `brand.json` 用于解决方案中每个应用程序的文件。

<a name="brandjson"></a>

## 品牌. json (中文(简体) ).

 文件结构 :

```json
{
  "Brand": {
    "DisplayName": "OpenCRM",
    "PublicBaseUrl": "https://localhost:7142",
    "TelegramUrl": "",
    "VkUrl": "",
    "InstagramUrl": ""
  }
}
```

- `DisplayName` ——界面中的项目名称,文件和通知模板.
- `PublicBaseUrl` ——公共HTTPS URL,包括基础路径;用于构建犬形URL,机器人,站点映射和支付回放URL.
- `TelegramUrl`, `VkUrl`, `InstagramUrl` ——社区链接,在配置时.

 此文件不包含任何秘密, 也绝不能包含它们: 提供者密钥, 连接字符串, 电子邮件发送地址在每个应用程序中分别配置 。  为本地机密推荐环境变量.  每个项目 `dotnet user-secrets` 保持单独:WebApp和EF项目没有共享的用户secretsId.

<a name="how-the-file-is-used"></a>

## 文件是如何使用的

共享的 `Helpers.ProjectBrand` 类读取文件。 WebApp、 通知、 付款和桌面将它作为链接文件包含, 并命名 `brand.json`  在输出目录中。 服务器进程支持 `Brand__DisplayName` 和 `Brand__PublicBaseUrl` 环境变量; 桌面应用程序读取共享的JSON文件.

更改后 `brand.json`,重建所需的应用程序或向每个进程的发射目录分发同一份文件版本。

更改设置后重新启动使用设置的过程 。
