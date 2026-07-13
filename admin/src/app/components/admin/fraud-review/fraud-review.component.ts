import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FraudService } from '../../../services/fraud/fraud.service';

@Component({
  selector: 'app-fraud-review',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './fraud-review.component.html',
  styleUrl: './fraud-review.component.css'
})
export class FraudReviewComponent implements OnInit {
  private fraudService = inject(FraudService);

  flagged: any[] = [];
  filteredFlagged: any[] = [];
  filterDecision = 'ALL';
  searchQuery = '';
  isLoading = false;
  successMsg = '';
  errorMsg = '';

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    this.fraudService.getFlagged().subscribe({
      next: (data) => {
        this.flagged = data;
        this.applyFilter();
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  applyFilter() {
    let result = [...this.flagged];
    if (this.filterDecision !== 'ALL') result = result.filter(f => f.decision === this.filterDecision);
    if (this.searchQuery) {
      const q = this.searchQuery.toLowerCase();
      result = result.filter(f => f.orderNumber?.toLowerCase().includes(q) || f.customerName?.toLowerCase().includes(q) || f.customerPhone?.includes(q));
    }
    this.filteredFlagged = result;
  }

  resolve(item: any, decision: string) {
    this.fraudService.resolve(item.id, decision).subscribe({
      next: () => {
        this.successMsg = `Order ${item.orderNumber} marked as ${decision}.`;
        this.load();
        setTimeout(() => this.successMsg = '', 4000);
      },
      error: (e) => { this.errorMsg = e?.error?.message || 'Failed to update decision.'; }
    });
  }

  getDecisionClass(decision: string) {
    return decision === 'BLOCK' ? 'decision-block' : decision === 'PASS' ? 'decision-pass' : 'decision-review';
  }

  getFlagList(flags: string): string[] {
    return flags ? flags.split(',').filter(f => f) : [];
  }
}
