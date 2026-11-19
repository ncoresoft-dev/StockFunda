import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HeaderComponent } from '../header/header.component';
import { MarketNewsComponent } from '../market-news/market-news.component';

@Component({
  selector: 'app-news-page',
  standalone: true,
  imports: [CommonModule, HeaderComponent, MarketNewsComponent],
  templateUrl: './news-page.component.html',
  styleUrls: ['./news-page.component.css']
})
export class NewsPageComponent { }
