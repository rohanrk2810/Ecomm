import { Component, inject } from '@angular/core';
import { RuntimeConfigService } from '../../../../libs/runtime-config/runtime-config';

@Component({
  selector: 'commerce-operations-foundation',
  template: `
    <section class="hero" aria-labelledby="operations-title">
      <div class="hero-copy">
        <p class="eyebrow">Your store, connected</p>
        <h1 id="operations-title">{{ config.heading }}</h1>
        <p class="intro">{{ config.description }}</p>
        <div class="notice">
          <span class="notice-mark" aria-hidden="true">i</span>
          <div>
            <strong>Staff access is being prepared.</strong>
            <p>Account setup is required before store tools become available.</p>
          </div>
        </div>
      </div>
      <div class="illustration" role="img" aria-label="Store operations workspace illustration">
        <div class="orbit orbit-one"></div>
        <div class="orbit orbit-two"></div>
        <div class="bag">
          <div class="handle"></div>
          <div class="bag-front"><span>together.</span><small>STORE OPERATIONS</small></div>
        </div>
        <span class="spark spark-one" aria-hidden="true">✦</span>
        <div class="illustration-caption">A place to keep your store moving.</div>
      </div>
    </section>
    <section class="values" aria-label="Upcoming store tools">
      <article>
        <span class="value-number">01</span>
        <h2>Products &amp; pricing</h2>
        <p>One workspace for your catalogue and offers.</p>
      </article>
      <article>
        <span class="value-number">02</span>
        <h2>Stock &amp; freshness</h2>
        <p>Keep track of branch inventory, batches and expiry.</p>
      </article>
      <article>
        <span class="value-number">03</span>
        <h2>Orders &amp; handover</h2>
        <p>Coordinate the journey from picking to dispatch.</p>
      </article>
    </section>
  `,
})
export class OperationsFoundationPage {
  protected readonly config = inject(RuntimeConfigService).current;
}
