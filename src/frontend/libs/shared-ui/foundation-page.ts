import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RuntimeConfigService } from '../runtime-config/runtime-config';

@Component({
  imports: [RouterLink],
  selector: 'commerce-foundation-page',
  template: `
    <section class="hero" aria-labelledby="hero-title">
      <div class="hero-copy">
        <p class="eyebrow">A fresh beginning</p>
        <h1 id="hero-title">{{ config.heading }}</h1>
        <p class="intro">{{ config.description }}</p>
        <div class="notice">
          <span class="notice-mark" aria-hidden="true">i</span>
          <div>
            <strong>Shopping opens soon.</strong>
            <p>Create your account while we prepare your shopping experience.</p>
            <a routerLink="/register">Create an account →</a>
          </div>
        </div>
      </div>
      <div
        class="illustration"
        role="img"
        aria-label="Illustration of a grocery bag with fresh produce"
      >
        <div class="orbit orbit-one"></div>
        <div class="orbit orbit-two"></div>
        <div class="leaf leaf-one"></div>
        <div class="leaf leaf-two"></div>
        <div class="bag">
          <div class="handle"></div>
          <div class="produce pear"></div>
          <div class="produce tomato"></div>
          <div class="produce baguette"></div>
          <div class="bag-front"><span>fresh.</span><small>EVERYDAY ESSENTIALS</small></div>
        </div>
        <span class="spark spark-one">✦</span><span class="spark spark-two">✦</span>
        <div class="illustration-caption">Good things are on their way.</div>
      </div>
    </section>
    <section class="values" aria-label="Our shopping experience">
      <article>
        <span class="value-number">01</span>
        <h2>Find your essentials</h2>
        <p>A simple place for your everyday grocery shop.</p>
      </article>
      <article>
        <span class="value-number">02</span>
        <h2>Shop with confidence</h2>
        <p>Clear product information and order updates.</p>
      </article>
      <article>
        <span class="value-number">03</span>
        <h2>Stay in the loop</h2>
        <p>Follow your order from confirmation to handover.</p>
      </article>
    </section>
  `,
})
export class FoundationPage {
  protected readonly config = inject(RuntimeConfigService).current;
}
