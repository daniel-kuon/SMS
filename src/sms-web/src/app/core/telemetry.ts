import { WebTracerProvider, BatchSpanProcessor } from '@opentelemetry/sdk-trace-web';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';
import { DocumentLoadInstrumentation } from '@opentelemetry/instrumentation-document-load';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { ATTR_SERVICE_NAME, ATTR_SERVICE_VERSION } from '@opentelemetry/semantic-conventions';

/**
 * Browser telemetry: page loads and every fetch to /api become spans in SigNoz (service "sms-web").
 * The OTLP/HTTP endpoint is reached through the dev proxy (/otlp → SigNoz collector :4318), so the
 * trace context (traceparent) propagates into the API and the whole request shows as one trace.
 */
export function initTelemetry(): void {
  const provider = new WebTracerProvider({
    resource: resourceFromAttributes({
      [ATTR_SERVICE_NAME]: 'sms-web',
      [ATTR_SERVICE_VERSION]: '0.1.0',
      'deployment.environment': 'development',
    }),
    spanProcessors: [new BatchSpanProcessor(new OTLPTraceExporter({ url: '/otlp/v1/traces' }))],
  });
  provider.register();
  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new FetchInstrumentation({
        ignoreUrls: [/\/otlp\//],
        propagateTraceHeaderCorsUrls: [/^\/api\//],
        clearTimingResources: true,
      }),
    ],
  });
}
