namespace Backend.Infrastructure.Hosting;

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
            // Must run before anything reads the scheme or the client address: behind the proxy
            // Kestrel sees plain http and the balancer's IP, so redirection would loop and the
            // login limiter would bucket every client together.
            app.UseForwardedHeaders();

            // Skipped in Development on purpose: the Vite proxy forwards to http://localhost:5244,
            // and a 307 to the HTTPS endpoint would break it on the dev certificate.
            app.UseHttpsRedirection();
        }

        // The SPA lives in wwwroot in the container image; in Development it is absent and Vite
        // serves it instead.
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
