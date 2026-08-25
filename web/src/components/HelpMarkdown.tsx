import ReactMarkdown, { type Components } from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeRaw from 'rehype-raw';
import rehypeSanitize from 'rehype-sanitize';

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

// rehypeRaw must run before rehypeSanitize: it's what turns the raw <br> tags this content
// relies on into real hast nodes in the first place, and sanitize is what keeps that raw-HTML
// door from becoming a stored-XSS hole if either content source ever stops being static .md
// files bundled at build time.
export function HelpMarkdown({ children }: { children: string }) {
  return (
    <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeRaw, rehypeSanitize]} components={components}>
      {children}
    </ReactMarkdown>
  );
}
