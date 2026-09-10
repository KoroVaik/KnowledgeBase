namespace KnowledgeBase.Api.Infrastructure.Hosting;

public static class MiddlewarePipeline
{
    public static WebApplication UseKnowledgeBasePipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        else
        {
            // Before anything reads the scheme or client IP: behind the proxy Kestrel sees http
            // and the balancer IP, so redirects loop and the login limiter buckets everyone.
            app.UseForwardedHeaders();

            // Skipped in Development: a 307 to https would break the Vite proxy on the dev cert.
            app.UseHttpsRedirection();
        }

        // SPA is in wwwroot in the image; in Development Vite serves it instead.
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
