type Child = Node | string | number | null | undefined | false;
type Children = Child | readonly Children[];

/**
 * Attributes, set as they are given: `true` sets an empty attribute and `false`, `null` or `undefined` leave it out.
 * `value` and `checked` set the property, so they show in forms; functions under `on…` names become event listeners.
 */
export type Props = Record<string, string | number | boolean | null | undefined | ((event: Event) => void)>;

/** An element built from data, whose text never becomes markup. */
export function h<K extends keyof HTMLElementTagNameMap>(tag: K, props: Props = {}, ...children: Children[]): HTMLElementTagNameMap[K] {
  const element = document.createElement(tag);
  for (const [name, value] of Object.entries(props)) {
    if (typeof value === "function") {
      element.addEventListener(name.slice(2).toLowerCase(), value);
    } else if (name === "value") {
      (element as unknown as HTMLInputElement).value = String(value ?? "");
    } else if (name === "checked") {
      (element as unknown as HTMLInputElement).checked = value === true;
    } else if (value === true) {
      element.setAttribute(name, "");
    } else if (value !== false && value !== null && value !== undefined) {
      element.setAttribute(name, String(value));
    }
  }

  append(element, children);
  return element;
}

function append(parent: Node, children: Children): void {
  if (Array.isArray(children)) {
    for (const child of children) {
      append(parent, child as Children);
    }
  } else if (children instanceof Node) {
    parent.appendChild(children);
  } else if (children !== null && children !== undefined && children !== false) {
    parent.appendChild(document.createTextNode(String(children)));
  }
}
