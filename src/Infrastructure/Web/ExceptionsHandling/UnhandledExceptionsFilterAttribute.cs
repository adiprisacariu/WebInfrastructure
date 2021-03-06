﻿namespace Skeleton.Web.ExceptionsHandling
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http.Extensions;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Serialization.JsonNet.Configuration;

    public class UnhandledExceptionsFilterAttribute : ActionFilterAttribute
    {
        private class ExceptionHandlingContext
        {
            public ActionExecutingContext ExecutingContext { get; }
            public ActionExecutedContext ExecutedContext { get; }

            public ExceptionHandlingContext(ActionExecutingContext executingContext, ActionExecutedContext executedContext)
            {
                ExecutingContext = executingContext;
                ExecutedContext = executedContext;
            }
        }

        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly ILogger _logger;

        private readonly (Type, Action<ExceptionHandlingContext>)[] _handlers;
        private readonly JsonSerializerSettings _jsonSerializerSettings;

        private void HandleOperationCanceledException(ExceptionHandlingContext context)
        {
            _logger.LogWarning("Request was cancelled");

            context.ExecutedContext.ExceptionHandled = true;
            context.ExecutedContext.Result = new StatusCodeResult((int)HttpStatusCode.BadRequest);
        }

        private void HandleException(ExceptionHandlingContext context)
        {
            const string message = "Unhandled exception has occurred";

            var messageBuilder = new StringBuilder().AppendLine(message);
            if (context.ExecutingContext.HttpContext?.Request != null)
            {
                var request = context.ExecutingContext.HttpContext.Request;
                messageBuilder.AppendLine($"Method: {request.Method}");
                messageBuilder.AppendLine($"Uri: {request.GetDisplayUrl()}");
                if (request.Headers != null && request.Headers.Count > 0)
                {
                    messageBuilder.AppendLine("Headers:");
                    foreach (var header in request.Headers)
                        messageBuilder.AppendLine($"\t{header.Key}:{header.Value}");
                }
            }
            if (context.ExecutingContext.ActionArguments != null && context.ExecutingContext.ActionArguments.Count > 0)
            {
                messageBuilder.AppendLine("Parameters:");
                foreach (var argument in context.ExecutingContext.ActionArguments)
                {
                    messageBuilder.AppendLine($"\t{argument.Key}:");
                    messageBuilder.AppendLine($"{JsonConvert.SerializeObject(argument.Value, _jsonSerializerSettings)}");
                }
            }
            _logger.LogError(context.ExecutedContext.Exception, messageBuilder.ToString());

            context.ExecutedContext.ExceptionHandled = true;
            context.ExecutedContext.Result = _hostingEnvironment.IsDevelopment() || _hostingEnvironment.IsStaging()
                ? new ObjectResult(new ApiErrorResponse(message, context.ExecutedContext.Exception))
                  {
                      DeclaredType = typeof(ApiErrorResponse),
                      StatusCode = (int)HttpStatusCode.InternalServerError
                  }
                : new ObjectResult(message) { DeclaredType = typeof(string), StatusCode = (int)HttpStatusCode.InternalServerError };
        }

        public UnhandledExceptionsFilterAttribute(IHostingEnvironment hostingEnvironment, ILogger<UnhandledExceptionsFilterAttribute> logger)
        {
            if (hostingEnvironment == null)
                throw new ArgumentNullException(nameof(hostingEnvironment));
            if(logger == null)
                throw new ArgumentNullException(nameof(logger));

            _hostingEnvironment = hostingEnvironment;
            _logger = logger;

            _handlers =
                new[]
                {
                    (typeof(OperationCanceledException), HandleOperationCanceledException),
                    (typeof(Exception), new Action<ExceptionHandlingContext>(HandleException))
                };
            _jsonSerializerSettings = new JsonSerializerSettings().UseDefaultSettings();
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var actionExecutedContext = await next();
            if(actionExecutedContext.Exception == null)
                return;

            var exceptionType = actionExecutedContext.Exception.GetType();
            _handlers
                .FirstOrDefault(x => x.Item1.IsAssignableFrom(exceptionType))
                .Item2(new ExceptionHandlingContext(context, actionExecutedContext));
        }
    }
}