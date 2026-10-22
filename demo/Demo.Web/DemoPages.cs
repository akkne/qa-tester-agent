using System.Net;

namespace Demo.Web;

public static class DemoPages
{
    public static string Home() => Layout("Демо регистрации", """
        <h1 data-testid="page-title">Демо регистрации</h1>
        <p>Создайте учётную запись и откройте личный кабинет.</p>
        <nav><a data-testid="register-link" href="/register">Регистрация</a><a data-testid="login-link" href="/login">Вход</a></nav>
        """);

    public static string Register(string token, string? error = null) => Layout("Регистрация", $$"""
        <h1 data-testid="page-title">Регистрация</h1>
        {{Error(error)}}
        <form method="post" action="/register" data-testid="register-form">
          <input type="hidden" name="__RequestVerificationToken" value="{{Encode(token)}}">
          <label for="email">Email</label>
          <input id="email" name="email" type="email" autocomplete="email" required data-testid="email-input">
          <label for="password">Пароль (не менее 12 символов)</label>
          <input id="password" name="password" type="password" autocomplete="new-password" minlength="12" required data-testid="password-input">
          <button type="submit" data-testid="register-submit">Зарегистрироваться</button>
        </form>
        <p>Уже есть учётная запись? <a data-testid="login-link" href="/login">Войти</a></p>
        """);

    public static string Login(string token, bool registered = false, string? error = null) => Layout("Вход", $$"""
        <h1 data-testid="page-title">Вход</h1>
        {{(registered ? "<p role=\"status\" data-testid=\"success-message\">Регистрация завершена. Теперь войдите.</p>" : "")}}
        {{Error(error)}}
        <form method="post" action="/login" data-testid="login-form">
          <input type="hidden" name="__RequestVerificationToken" value="{{Encode(token)}}">
          <label for="email">Email</label>
          <input id="email" name="email" type="email" autocomplete="email" required data-testid="email-input">
          <label for="password">Пароль</label>
          <input id="password" name="password" type="password" autocomplete="current-password" required data-testid="password-input">
          <button type="submit" data-testid="login-submit">Войти</button>
        </form>
        <p>Нет учётной записи? <a data-testid="register-link" href="/register">Зарегистрироваться</a></p>
        """);

    public static string Cabinet(Account account, string token) => Layout("Личный кабинет", $$"""
        <h1 data-testid="page-title">Личный кабинет</h1>
        <p data-testid="account-email">{{Encode(account.Email)}}</p>
        <p><a data-testid="account-link" href="/cabinet/{{account.Id}}">Постоянная ссылка на кабинет</a></p>
        <form method="post" action="/logout">
          <input type="hidden" name="__RequestVerificationToken" value="{{Encode(token)}}">
          <button type="submit" data-testid="logout-submit">Выйти</button>
        </form>
        """);

    private static string Error(string? error) => error is null ? "" : $"<p role=\"alert\" data-testid=\"form-error\">{Encode(error)}</p>";
    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Layout(string title, string content) => $$"""
        <!doctype html>
        <html lang="ru">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{Encode(title)}}</title>
          <style>
            body { font: 1rem/1.5 system-ui, sans-serif; margin: 0; background: #f5f7fb; color: #1d2738; }
            main { box-sizing: border-box; width: min(100% - 2rem, 30rem); margin: 3rem auto; padding: 1.5rem; background: white; border-radius: .75rem; box-shadow: 0 2px 14px #14213d18; }
            h1 { margin-top: 0; } form { display: grid; gap: .7rem; } label { font-weight: 600; }
            input, button { box-sizing: border-box; width: 100%; padding: .7rem; font: inherit; }
            button { margin-top: .4rem; background: #2457c5; color: white; border: 0; border-radius: .35rem; cursor: pointer; }
            nav { display: flex; gap: 1.5rem; } [role="alert"] { color: #a11919; } [role="status"] { color: #175b2b; }
          </style>
        </head>
        <body><main>{{content}}</main></body>
        </html>
        """;
}
