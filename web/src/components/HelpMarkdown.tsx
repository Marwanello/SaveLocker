import ReactMarkdown, { type Components } from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeRaw from 'rehype-raw';

// Wraps <table> in its own scrollable div instead of making the table element itself
// `overflow-x: auto`, which would strip its implicit ARIA table semantics in browsers
// that compute accessible roles from computed `display` (notably Safari/VoiceOver).
const components: Components = {
  table: ({ node: _node, ...props }) => (
    <div className="help-content-table-wrap">
      <table {...props} />
    </div>
  ),
};

export function HelpMarkdown({ children }: { children: string }) {
  return (
    <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeRaw]} components={components}>
      {children}
    </ReactMarkdown>
  );
}
